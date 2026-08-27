using Datadog.Trace;

namespace TrmnlApi.Observability;

/// <summary>
/// The span every custom tag goes on: the automatically instrumented <c>aspnet_core.request</c>
/// span for the request being served.
/// </summary>
/// <remarks>
/// This service used to start its own spans and tag those. Measured against a real trace, they
/// earned nothing: the wrapping span covered 892ms of a 1004ms request, so it timed the entry span
/// over again, and the calls worth timing separately - each Open-Meteo request - already get their
/// own client spans from the HTTP instrumentation.
/// <para>
/// Tagging the entry span instead makes <c>http.route</c>, <c>http.status_code</c> and the
/// <c>weather.*</c> tags facets of one span, so questions that cross them stop being trace-level
/// queries. It also puts the error flag where error rate natively reads it, which is what lets a
/// deliberately 200-carrying failure still count as a failure.
/// </para>
/// <para>
/// Null when nothing is tracing, which is the normal case in tests. Every call site has to be
/// null-safe; nothing here is worth failing a request over.
/// </para>
/// </remarks>
public static class RequestSpan
{
    public static ISpan? Current => Tracer.Instance.ActiveScope?.Span;
}
