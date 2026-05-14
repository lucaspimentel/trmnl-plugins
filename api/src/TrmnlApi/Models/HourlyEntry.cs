using System.Text.Json.Serialization;

namespace TrmnlApi.Models;

public record HourlyEntry(
    string Time,
    string Label,
    int Temperature,
    [property: JsonPropertyName("precipitation_probability")] int PrecipitationProbability,
    [property: JsonPropertyName("icon_class")] string IconClass,
    [property: JsonPropertyName("is_day")] bool IsDay
);
