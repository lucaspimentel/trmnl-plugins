using System.Text.Json.Serialization;

namespace TrmnlApi.Models;

public record DailyEntry(
    string Date,
    int High,
    int Low,
    string Condition,
    [property: JsonPropertyName("icon_class")] string IconClass,
    [property: JsonPropertyName("precipitation_probability")] int PrecipitationProbability,
    string Sunrise,
    string Sunset
);
