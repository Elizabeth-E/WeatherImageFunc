using System.Net;
using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace WeatherImageFunc.Functions
{
    public class GetJobStatus
    {
        private readonly ILogger _logger;
        private readonly BlobServiceClient _blobServiceClient;

        public GetJobStatus(ILoggerFactory loggerFactory, BlobServiceClient blobServiceClient)
        {
            _logger = loggerFactory.CreateLogger<GetJobStatus>();
            _blobServiceClient = blobServiceClient;
        }

        [Function("GetJobStatus")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get",
                Route = "jobs/{jobId}/status")] HttpRequestData req,
            string jobId)
        {
            _logger.LogInformation("Status request for job {jobId}", jobId);

            var container = _blobServiceClient.GetBlobContainerClient("weather-images");
            await container.CreateIfNotExistsAsync();

            // status.json lives on the blob under jobs/jobId/
            var statusBlob = container.GetBlobClient($"jobs/{jobId}/status.json");

            if (!await statusBlob.ExistsAsync())
            {
                var nf = req.CreateResponse(HttpStatusCode.NotFound);
                await nf.WriteAsJsonAsync(new
                {
                    jobId,
                    state = "not_found"
                });
                return nf;
            }

            // read status.json
            var download = await statusBlob.DownloadContentAsync();
            var status = JsonSerializer.Deserialize<JobStatus>(download.Value.Content.ToStream()) ?? new JobStatus();

            int total = status.Total;

            // count how many blobs exist in this job folder
            int done = 0;
            await foreach (var blobItem in container.GetBlobsAsync(prefix: $"{jobId}/"))
                done++;

            double percent = total > 0 ? (double)done / total * 100.0 : 0.0;
            bool completed = (done == total);

            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(new
            {
                jobId,
                total,
                done,
                percent = Math.Round(percent, 1),
                completed,
                resultsUrl = $"/api/jobs/{jobId}/images"
            });

            return resp;
        }
    }
}
