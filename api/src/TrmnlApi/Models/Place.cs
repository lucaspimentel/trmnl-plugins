namespace TrmnlApi.Models;

/// <summary>
/// The place a v2 forecast is actually for, echoed back so the screen can show it.
/// </summary>
/// <remarks>
/// This is the mitigation for the two silent wrong-place failures the design note records: the
/// geocoder takes the most prominent match, so someone who types "Portland" and means Maine, or
/// "75001" and means Texas, can see which one they got instead of inferring it from a suspicious
/// temperature. It is also the only way the plugin can display the resolved location at all, since
/// a template cannot read its own custom field.
/// <para>
/// <see cref="Admin1"/> is a display name ("Massachusetts"), never an ISO 3166-2 code: Open-Meteo
/// does not carry one. A subdivision code can only come from the polygon lookup in
/// docs/geographic-telemetry.md.
/// </para>
/// </remarks>
public record Place(
    string Name,
    string? Admin1,
    string? Country,
    string? CountryCode,
    double Latitude,
    double Longitude
);
