using System.Net;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Azure.Storage.Sas;

namespace WeatherImageFunc.Functions
{
    public class GetImagesForJob
    {
        private readonly ILogger _logger;
        private readonly BlobServiceClient _blobServiceClient;

        public GetImagesForJob(ILoggerFactory loggerFactory, BlobServiceClient blobServiceClient)
        {
            _logger = loggerFactory.CreateLogger<GetImagesForJob>();
            _blobServiceClient = blobServiceClient;
        }

        [Function("GetImagesForJob")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get",
                Route = "jobs/{jobId}/images")] HttpRequestData req,
            string jobId)
        {
            _logger.LogInformation("Fetch images for job {jobId}", jobId);

            var container = _blobServiceClient.GetBlobContainerClient("weather-images");
            await container.CreateIfNotExistsAsync();

            var prefix = $"{jobId}/";

            List<string> urls = new();

            await foreach (var blobItem in container.GetBlobsAsync(prefix: prefix))
            {
                var blobClient = container.GetBlobClient(blobItem.Name);

                var sasUri = blobClient.GenerateSasUri(
                    BlobSasPermissions.Read,
                    DateTimeOffset.UtcNow.AddMinutes(60));

                urls.Add(sasUri.ToString());
            }


            var response = req.CreateResponse(HttpStatusCode.OK);

            await response.WriteAsJsonAsync(new
            {
                jobId,
                count = urls.Count,
                images = urls
            });

            return response;
        }
    }
}
