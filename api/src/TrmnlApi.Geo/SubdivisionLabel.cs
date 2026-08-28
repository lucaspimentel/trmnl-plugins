namespace TrmnlApi.Geo;

/// <summary>
/// Turns an ISO 3166-2 code and its display name into the short label the title bar shows.
/// </summary>
/// <remarks>
/// 56% of the subdivision codes in the bundled dataset are alphabetic, and those are what people
/// recognise: <c>US-MA</c> reads as "MA". The rest are numeric - France and Japan among them - and
/// "Paris, 75" is not a location anyone recognises, so those fall back to the name.
/// <para>
/// The United Kingdom is the exception that is neither. Its 232 features are districts, so the
/// code path would render "Cambridge, CAM". The name is longer but at least true, and the
/// template's 18-character rule drops it when it does not fit.
/// </para>
/// </remarks>
public static class SubdivisionLabel
{
    /// <summary>
    /// Countries whose subdivision codes are alphabetic but not recognisable, so the name wins.
    /// </summary>
    private static readonly HashSet<string> NameFirstCountries = new(StringComparer.OrdinalIgnoreCase) { "GB" };

    public static string? Short(string? isoCode, string? displayName)
    {
        if (string.IsNullOrEmpty(isoCode))
        {
            return string.IsNullOrEmpty(displayName) ? null : displayName;
        }

        var dash = isoCode.IndexOf('-');
        if (dash < 0 || dash == isoCode.Length - 1)
        {
            return string.IsNullOrEmpty(displayName) ? null : displayName;
        }

        var country = isoCode[..dash];
        var suffix = isoCode[(dash + 1)..];

        if (NameFirstCountries.Contains(country) && !string.IsNullOrEmpty(displayName))
        {
            return displayName;
        }

        foreach (var c in suffix)
        {
            if (!char.IsAsciiLetter(c))
            {
                return string.IsNullOrEmpty(displayName) ? null : displayName;
            }
        }

        return suffix;
    }
}
