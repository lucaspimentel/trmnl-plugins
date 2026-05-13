using System.Text.Json.Serialization;

namespace TrmnlApi.Models;

public record Meta(
    string Cache,
    [property: JsonPropertyName("fetched_at")] DateTimeOffset FetchedAt,
    [property: JsonPropertyName("data_time")] string DataTime,
    [property: JsonPropertyName("served_at")] DateTimeOffset ServedAt,
    [property: JsonPropertyName("age_seconds")] long AgeSeconds,
    Upstream? Upstream
);

public record Upstream(int? Status, string? Error);
