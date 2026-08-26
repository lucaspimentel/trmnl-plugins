using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace TrmnlApi.Observability;

// Owns the log site for exceptions no endpoint handled, so that shipping them to a log backend is a
// matter of allowing this category rather than naming whichever framework category happens to catch
// them. Without this handler they surface under a Kestrel category that has moved between releases
// and that also carries unrelated connection-level errors.
//
// The request path is logged but never the query string: coordinates are PII and appear there.
public sealed class UnhandledExceptionLogger(ILogger<UnhandledExceptionLogger> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(
            exception,
            "Unhandled exception serving {Method} {Path}",
            httpContext.Request.Method,
            httpContext.Request.Path.Value);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "text/plain";
        await httpContext.Response.WriteAsync("Internal server error.", cancellationToken);
        return true;
    }
}
