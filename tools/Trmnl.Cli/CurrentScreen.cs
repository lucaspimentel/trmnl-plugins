using System.Text.Json.Serialization;

namespace Trmnl.Cli;

public class CurrentScreen
{
    [JsonPropertyName("status")]
    public int Status { get; init; }

    [JsonPropertyName("refresh_rate")]
    public int RefreshRate { get; init; }

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; init; }

    [JsonPropertyName("filename")]
    public string? Filename { get; init; }

    [JsonPropertyName("rendered_at")]
    public object? RenderedAt { get; init; }
}
