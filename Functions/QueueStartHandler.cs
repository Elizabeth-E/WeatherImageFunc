using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace WeatherImageFunc.Functions
{
    public class QueueStartHandler
    {
        private readonly ILogger _logger;
        private readonly HttpClient _http = new HttpClient();

        public QueueStartHandler(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<QueueStartHandler>();
        }

        [Function("QueueStartHandler")]
        [QueueOutput("image-process")]
        public async Task<string[]> Run(
            [QueueTrigger("image-start")] string message)
        {
            _logger.LogInformation("QueueStartHandler triggered with message: {0}", message);

            var obj = JsonSerializer.Deserialize<JsonElement>(message);
            string jobId = obj.GetProperty("JobId").GetString();

            // fetch buienradar feed
            string url = Environment.GetEnvironmentVariable("BUIENRADAR_API");
            var json = await _http.GetStringAsync(url);

            var doc = JsonDocument.Parse(json);

            // select 50 stations
            var stations = doc.RootElement
                .GetProperty("actual")
                .GetProperty("stationmeasurements")
                .EnumerateArray()
                .Take(50)
                .ToList();

            //gets info from buienradar api and outputs it to the queue to later be added to an image
            var outputTasks = stations.Select(s =>
            {
                return JsonSerializer.Serialize(new ImageTaskMessage
                {
                    JobId = jobId,
                    StationId = s.GetProperty("stationid").GetInt32(),
                    StationName = s.GetProperty("stationname").GetString(),
                    Temperature = s.TryGetProperty("temperature", out var tempProp) && tempProp.ValueKind == JsonValueKind.Number
                        ? tempProp.GetDouble()
                            :  0.0,

                    Description = s.TryGetProperty("weatherdescription", out var descProp) && descProp.ValueKind == JsonValueKind.String
                        ? descProp.GetString()
                        : s.TryGetProperty("weatherdescriptionlong", out var descLongProp) && descLongProp.ValueKind == JsonValueKind.String
                            ? descLongProp.GetString()
                            : "unknown"

                });
            })
            .ToArray();

            _logger.LogInformation("Fan-out: Sending {0} messages to image-process queue", outputTasks.Length);

            // INIT job status.json under the same container you use for images
            string storageConn = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            string containerName = Environment.GetEnvironmentVariable("OUTPUT_BLOB_CONTAINER") ?? "weather-images";
            var statusContainer = new Azure.Storage.Blobs.BlobContainerClient(storageConn, containerName);
            await statusContainer.CreateIfNotExistsAsync();

            var statusBlob = statusContainer.GetBlobClient($"jobs/{jobId}/status.json");

            // total = stations.Count; done = 0
            var init = new JobStatus { Total = stations.Count, Done = 0 };
            using (var ms = new MemoryStream(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(init)))
            {
                await statusBlob.UploadAsync(ms, overwrite: true);
            }


            return outputTasks;
        }
    }
}
