
using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;

namespace WeatherImageFunc.Functions
{
    public class ProcessImageTask
    {
        private readonly ILogger _logger;
        private readonly HttpClient _http = new HttpClient();

        public ProcessImageTask(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<ProcessImageTask>();
        }

        [Function("ProcessImageTask")]
        public async Task Run(
            [QueueTrigger("image-process")] string message)
        {
            var msg = JsonSerializer.Deserialize<ImageTaskMessage>(message);

            var weatherDescription = msg.Description;

            //setting up cache to store images so weather with the same description goes on the same image.
            //this is to limit eating up too much quota in Pexels API.
            string conn = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            var cacheContainer = new BlobContainerClient(conn, "background-cache");
            await cacheContainer.CreateIfNotExistsAsync();

            string slug = weatherDescription.ToLowerInvariant().Replace(" ", "_") + ".jpg";
            var cacheBlob = cacheContainer.GetBlobClient(slug);

            Stream imageStream;

            //if an image already exists for a specific weather description itll reuse the image otherwise it gets a new image from Pexels
            if (await cacheBlob.ExistsAsync())
            {
                imageStream = await cacheBlob.OpenReadAsync();
                _logger.LogInformation($"CACHE HIT for '{weatherDescription}'");
            }
            else
            {
            _logger.LogInformation($"CACHE MISS for '{weatherDescription}' downloading from Pexels");

            string pexelsKey = Environment.GetEnvironmentVariable("PEXELS_API_KEY");

            var req = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.pexels.com/v1/search?query={Uri.EscapeDataString(weatherDescription)}&per_page=1"
            );
            req.Headers.Add("Authorization", pexelsKey);

            var res = await _http.SendAsync(req);
            res.EnsureSuccessStatusCode();

            var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var photo = json.RootElement.GetProperty("photos")[0];
            string url = photo.GetProperty("src").GetProperty("large").GetString();

            var bytes = await _http.GetByteArrayAsync(url);

            // saves image for new weather description in the cache
            using (var msUpload = new MemoryStream(bytes))
                await cacheBlob.UploadAsync(msUpload, overwrite: true);

            imageStream = new MemoryStream(bytes);
            }

            // use imageStream as the image source
            using var image = Image.Load(imageStream);

            image.Mutate(x => x.Resize(1024, 768));

            // text lines to write on the image
            string line1 = msg.StationName;
            string line2 = $"{msg.Temperature:F1} °C";
            string line3 = msg.Description;

            //shipping font with function to ensure the font is always available
            var fontPath = Path.Combine(Environment.CurrentDirectory, "Fonts", "OpenSans-Regular.ttf");

            var fonts = new FontCollection();
            var family = fonts.Install(fontPath);
            var font   = family.CreateFont(48);

            image.Mutate(x => {
                x.DrawText(line1, font, Color.White, new PointF(40, 40));
                x.DrawText(line2, font, Color.White, new PointF(40, 110));
                x.DrawText(line3, font, Color.White, new PointF(40, 180));
            });


            // output onto the blob
            string storageConn = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            string containerName = Environment.GetEnvironmentVariable("OUTPUT_BLOB_CONTAINER") ?? "weather-images";
            var blob = new BlobContainerClient(storageConn, containerName);
            await blob.CreateIfNotExistsAsync();

            string blobName = $"{msg.JobId}/{msg.StationId}.png";
            using var outStream = new MemoryStream();
            image.SaveAsPng(outStream);
            outStream.Position = 0;

            await blob.UploadBlobAsync(blobName, outStream);

            _logger.LogInformation($"image created: {blobName}");
        }
    }
}
