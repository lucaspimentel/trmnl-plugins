namespace TrmnlApi.Geo;

/// <summary>
/// Reads the country the caller declared, in any of the forms the plugin's dropdown can produce.
/// </summary>
/// <remarks>
/// The dropdown's option values carry a label - <c>US - United States of America</c> - and
/// <c>polling_url</c> is supposed to cut that down to the code with a Liquid filter. Relying on
/// that was a mistake: a request arrived with the whole label, the strict two-letter rule rejected
/// it, and a user who had chosen their country was served as though they had not. It is not worth
/// knowing whether the filter, the saved setting, or something else was at fault, because the
/// value is unambiguous either way.
/// <para>
/// So a leading alpha-2 is taken whenever what follows is not another letter. That accepts
/// <c>US</c> and <c>US - United States of America</c>, and still rejects <c>Auto</c> and
/// <c>United States</c>, whose third character is a letter and which are therefore not a code with
/// something after it.
/// </para>
/// </remarks>
public static class CountryPreference
{
    /// <summary>The declared alpha-2 in upper case, or null when nothing usable was supplied.</summary>
    public static string? Parse(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var text = value.AsSpan().Trim();
        if (text.Length < 2 || !char.IsAsciiLetter(text[0]) || !char.IsAsciiLetter(text[1]))
        {
            return null;
        }

        // A third letter means this is a word, not a code with a label after it.
        if (text.Length > 2 && char.IsAsciiLetter(text[2]))
        {
            return null;
        }

        return text[..2].ToString().ToUpperInvariant();
    }
}
