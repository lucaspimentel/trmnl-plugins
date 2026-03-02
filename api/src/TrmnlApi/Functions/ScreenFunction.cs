using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace TrmnlApi.Functions;

public class ScreenFunction(IHttpClientFactory httpClientFactory, ILogger<ScreenFunction> logger)
{
    [Function("screen")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/screen")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var deviceId = Environment.GetEnvironmentVariable("TRMNL_DEVICE_ID");
        var apiKey = Environment.GetEnvironmentVariable("TRMNL_DEVICE_API_KEY");

        if (string.IsNullOrEmpty(deviceId) || string.IsNullOrEmpty(apiKey))
        {
            logger.LogError("TRMNL_DEVICE_ID or TRMNL_DEVICE_API_KEY environment variable is not configured");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteStringAsync("TRMNL_DEVICE_ID and TRMNL_DEVICE_API_KEY must be configured.", cancellationToken);
            return error;
        }

        var client = httpClientFactory.CreateClient("TrmnlApi");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://usetrmnl.com/api/current_screen");
        request.Headers.Add("ID", deviceId);
        request.Headers.Add("Access-Token", apiKey);

        HttpResponseMessage upstream;
        try
        {
            upstream = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to call TRMNL current_screen API");
            var error = req.CreateResponse(HttpStatusCode.BadGateway);
            await error.WriteStringAsync("Failed to reach TRMNL API.", cancellationToken);
            return error;
        }

        string? imageUrl = null;
        try
        {
            var body = await upstream.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("image_url", out var prop))
                imageUrl = prop.GetString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse TRMNL current_screen response");
        }

        if (string.IsNullOrEmpty(imageUrl))
        {
            var error = req.CreateResponse(HttpStatusCode.BadGateway);
            await error.WriteStringAsync("TRMNL API did not return an image_url.", cancellationToken);
            return error;
        }

        var redirect = req.CreateResponse(HttpStatusCode.Found);
        redirect.Headers.Add("Location", imageUrl);
        return redirect;
    }
}
