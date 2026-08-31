using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace TrmnlLegacyProxy;

/// <summary>
/// Forwards <c>/api/v1/forecast</c> from the original host to the current one.
/// </summary>
/// <remarks>
/// This host still carries real traffic from forked copies of the plugin, which point at it in
/// their own settings and cannot be updated by us. It is not a husk kept for sentiment: it was
/// measured serving thousands of requests a day. See <c>api/docs/legacy-host-proxy.md</c>.
/// <para>
/// The response has to be indistinguishable from what a caller got before, so this class relays
/// bytes and status codes rather than modelling anything. <b>Do not add features here.</b> Every
/// behaviour this file grows is a behaviour that has to be kept working for callers who can never
/// be told it changed.
/// </para>
/// </remarks>
public class ForecastProxy(IHttpClientFactory httpClientFactory, ILogger<ForecastProxy> logger)
{
    internal const string HttpClientName = "origin";

    /// <summary>Marks the request as arriving through the old hostname, for the receiver to tag on.</summary>
    private const string ProxyMarkerHeader = "X-Legacy-Proxy";

    /// <summary>
    /// Caller headers worth carrying across. The tracing ones keep the caller's own trace intact,
    /// which is what identified the caller in the first place.
    /// </summary>
    private static readonly string[] ForwardedHeaders =
        ["traceparent", "tracestate", "baggage", "sentry-trace"];

    [Function("forecast")]
    public async Task<IResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/forecast")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        // The query string is taken as written and never parsed. Rebuilding it from parsed values
        // would silently drop anything undocumented, and `fake=true` is exactly that.
        var target = "/api/v1/forecast" + request.QueryString.Value;

        using var forward = new HttpRequestMessage(HttpMethod.Get, target);
        foreach (var name in ForwardedHeaders)
        {
            if (request.Headers.TryGetValue(name, out var value))
            {
                forward.Headers.TryAddWithoutValidation(name, (string?)value);
            }
        }

        forward.Headers.TryAddWithoutValidation(ProxyMarkerHeader, "1");

        var client = httpClientFactory.CreateClient(HttpClientName);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(forward, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller went away. Not a failure of this service, and matching what the endpoint
            // being proxied already reports for the same case.
            return Results.StatusCode(499);
        }
        catch (Exception ex)
        {
            // The origin being unreachable is the failure mode this design accepts: the old host
            // used to answer independently and no longer can. Report it rather than dressing it up.
            logger.LogError(ex, "Forwarding to the origin failed");
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }

        return new RelayedResponse(response);
    }

    /// <summary>
    /// Writes the origin's status, content type and body back verbatim.
    /// </summary>
    /// <remarks>
    /// The body is copied as a stream and never deserialized. A round trip through a JSON model can
    /// reorder keys and reformat numbers: both are invisible when comparing parsed objects and
    /// perfectly visible to a caller comparing bytes.
    /// </remarks>
    internal sealed class RelayedResponse(HttpResponseMessage response) : IResult, IDisposable
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            using (response)
            {
                httpContext.Response.StatusCode = (int)response.StatusCode;

                if (response.Content.Headers.ContentType is { } contentType)
                {
                    httpContext.Response.ContentType = contentType.ToString();
                }

                await using var body = await response.Content.ReadAsStreamAsync(httpContext.RequestAborted);
                await body.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted);
            }
        }

        public void Dispose() => response.Dispose();
    }
}
