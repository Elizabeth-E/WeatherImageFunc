using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;


public class PexelsTestFunction
{
    private readonly HttpClient _http;
    private readonly string _pexelsKey;

    public PexelsTestFunction(IHttpClientFactory factory)
    {
 
        _http = factory.CreateClient();
        _pexelsKey = Environment.GetEnvironmentVariable("PEXELS_API_KEY") ?? throw new InvalidOperationException("PEXELS_API_KEY missing");

    }

    //function for testing how many calls are still allowed for Pexels API
   [Function("TestPexels")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.pexels.com/v1/search?query=cats");

        request.Headers.Add("Authorization", _pexelsKey);
        var res = await _http.SendAsync(request);

        var limit = res.Headers.GetValues("X-Ratelimit-Limit").FirstOrDefault();
        var remaining = res.Headers.GetValues("X-Ratelimit-Remaining").FirstOrDefault();

        Console.WriteLine($"pexels limit = {limit}");
        Console.WriteLine($"pexels remaining = {remaining}");

        var body = await res.Content.ReadAsStringAsync();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteStringAsync(body);
        return response;
    }

}
