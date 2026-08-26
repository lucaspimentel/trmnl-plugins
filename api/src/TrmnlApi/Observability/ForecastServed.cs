namespace TrmnlApi.Observability;

// Logger category for the one informational event worth shipping to Datadog. It exists so that
// "forecast served" can be allowed without also allowing the client-cancelled log that would
// otherwise share WeatherEndpoint's category.
//
// The allowlist that consumes this lives in appsettings.json under Logging:Datadog:LogLevel.
public sealed class ForecastServed;
