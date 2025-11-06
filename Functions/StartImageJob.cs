using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace WeatherImageFunc.Functions
{
    public class StartImageJob
    {
        private readonly ILogger _logger;

        public StartImageJob(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<StartImageJob>();
        }

        public class StartJobMessage
        {
            public string JobId { get; set; }
            public DateTime CreatedAt { get; set; }
            public string RequestedBy { get; set; }
        }

        //function used to create job id and put it onto the queue while also giving HTTP response
        [Function("StartImageJob")]
        [QueueOutput("image-start")]
        public async Task<string> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs/start")] HttpRequestData req)
        {
            _logger.LogInformation("StartImageJob triggered.");

            var jobId = Guid.NewGuid().ToString("N");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            string requestedBy = null;

            if (!string.IsNullOrWhiteSpace(requestBody))
            {
                try
                {
                    dynamic obj = JsonConvert.DeserializeObject(requestBody);
                    requestedBy = obj?.requestedBy;
                }
                catch { }
            }

            //this message will go to the queue
            var message = new StartJobMessage
            {
                JobId = jobId,
                CreatedAt = DateTime.UtcNow,
                RequestedBy = requestedBy
            };

            //respose to client
            var response = req.CreateResponse(System.Net.HttpStatusCode.Accepted);
            await response.WriteAsJsonAsync(new
            {
                jobId,
                statusUrl = $"/api/jobs/{jobId}/status",
                resultsUrl = $"/api/jobs/{jobId}/images"
            });

            return JsonConvert.SerializeObject(message);
        }
    }
}
