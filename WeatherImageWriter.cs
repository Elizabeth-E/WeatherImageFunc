using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Inholland.WeatherImageWriter;

public class WeatherImageWriter
{
    private readonly ILogger<WeatherImageWriter> _logger;

    public WeatherImageWriter(ILogger<WeatherImageWriter> logger)
    {
        _logger = logger;
    }

    [Function("WeatherImageWriter")]
    public IActionResult Run([HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequest req)
    {
        _logger.LogInformation("C# HTTP trigger function processed a request.");
        return new OkObjectResult("Welcome to Azure Functions!");
    }
}