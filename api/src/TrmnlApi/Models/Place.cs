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
/// Every field but <see cref="Name"/> now comes from the bundled dataset rather than from the
/// geocoder, on every path including a bare coordinate pair. That is what lets a coordinate caller
/// see a location at all, and what stops "Guayama" rendering as "Guayama, Guayama".
/// </para>
/// <para>
/// <see cref="Admin1"/> carries the best available <em>short</em> label: the alphabetic part of the
/// ISO 3166-2 code where there is one ("US-MA" gives "MA"), and the subdivision's display name
/// where the code is numeric ("FR-59" gives "Nord", never "59"). The full ISO code goes to
/// telemetry instead. See <c>TrmnlApi.Geo.SubdivisionLabel</c> and docs/geographic-telemetry.md.
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
