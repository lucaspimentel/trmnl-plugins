using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TrmnlApi.Functions;

public class ScreenFunction
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ScreenFunction> _logger;
    private readonly string? _deviceId;
    private readonly string? _apiKey;

    public ScreenFunction(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<ScreenFunction> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _deviceId = configuration["TRMNL_DEVICE_ID"];
        _apiKey = configuration["TRMNL_DEVICE_API_KEY"];
    }

    [Function("screen")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/screen")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_deviceId) || string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogError("TRMNL_DEVICE_ID or TRMNL_DEVICE_API_KEY environment variable is not configured");
            var error = req.CreateResponse(HttpStatusCode.InternalServerError);
            await error.WriteStringAsync("TRMNL_DEVICE_ID and TRMNL_DEVICE_API_KEY must be configured.", cancellationToken);
            return error;
        }

        var client = _httpClientFactory.CreateClient("TrmnlApi");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://usetrmnl.com/api/current_screen");
        request.Headers.Add("ID", _deviceId);
        request.Headers.Add("Access-Token", _apiKey);

        HttpResponseMessage upstream;
        try
        {
            upstream = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call TRMNL current_screen API");
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
            _logger.LogError(ex, "Failed to parse TRMNL current_screen response");
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
