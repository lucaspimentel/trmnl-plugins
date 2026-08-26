using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TrmnlApi.Observability;

namespace TrmnlApi.Tests;

public class UnhandledExceptionLoggerTests
{
    [Fact]
    public async Task TryHandleAsync_LogsTheExceptionAndWritesA500()
    {
        var logger = new CapturingLogger();
        var handler = new UnhandledExceptionLogger(logger);
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/v1/forecast";
        var body = new MemoryStream();
        context.Response.Body = body;
        var boom = new InvalidOperationException("boom");

        var handled = await handler.TryHandleAsync(context, boom, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal("Internal server error.", Encoding.UTF8.GetString(body.ToArray()));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(boom, entry.Exception);
        Assert.Contains("/api/v1/forecast", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryHandleAsync_DoesNotLogTheQueryStringBecauseCoordinatesArePii()
    {
        var logger = new CapturingLogger();
        var handler = new UnhandledExceptionLogger(logger);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/forecast";
        context.Request.QueryString = new QueryString("?latitude=42.3601&longitude=-71.0589");
        context.Response.Body = new MemoryStream();

        await handler.TryHandleAsync(context, new InvalidOperationException("boom"), CancellationToken.None);

        Assert.DoesNotContain("42.3601", Assert.Single(logger.Entries).Message, StringComparison.Ordinal);
    }

    private sealed class CapturingLogger : ILogger<UnhandledExceptionLogger>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }
}
