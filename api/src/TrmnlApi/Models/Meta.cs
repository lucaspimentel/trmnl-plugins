using System.Text.Json.Serialization;

namespace TrmnlApi.Models;

public record Meta(
    string Cache,
    string Provider,
    [property: JsonPropertyName("requested_provider")] string RequestedProvider,
    [property: JsonPropertyName("fetched_at")] DateTimeOffset FetchedAt,
    [property: JsonPropertyName("data_time")] string DataTime,
    [property: JsonPropertyName("served_at")] DateTimeOffset ServedAt,
    [property: JsonPropertyName("age_seconds")] long AgeSeconds,
    [property: JsonPropertyName("time_format")] string TimeFormat,
    Upstream? Upstream,
    // v2 only. A display preference the template cannot read from its custom field, so it rides
    // back in the response like time_format does. Left null by v1, whose serialized bytes are
    // frozen, and omitted from the JSON entirely when null.
    [property: JsonPropertyName("abbreviate_days")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? AbbreviateDays = null
);

public record Upstream(int? Status, string? Error);
