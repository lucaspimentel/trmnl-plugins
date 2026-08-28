using System.Globalization;
using System.Text;

namespace TrmnlApi.Geo;

/// <summary>
/// The one normalization the dataset builder and the geocoder must agree on. If these two ever
/// drift, every accented name in the database becomes unreachable and nothing fails loudly.
/// </summary>
public static class GeoText
{
    /// <summary>
    /// Casefolds, strips diacritics and collapses whitespace, so "MÜNCHEN" and "Munchen" are the
    /// same key. Punctuation other than whitespace is kept: "St. Louis" and "Saint Louis" are
    /// separate GeoNames aliases and both are in the alias table.
    /// </summary>
    public static string Normalize(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = false;

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (builder.Length > 0 && !lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }
                continue;
            }

            builder.Append(c);
            lastWasSpace = false;
        }

        if (lastWasSpace && builder.Length > 0)
        {
            builder.Length--;
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Normalizes a postal code for the <c>postal</c> table: uppercase, no spaces or hyphens, so
    /// "M5V 3L9", "m5v3l9" and "M5V-3L9" are one key.
    /// </summary>
    public static string NormalizePostal(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(char.ToUpperInvariant(c));
            }
        }
        return builder.ToString();
    }

    /// <summary>
    /// True when the text could be a postal code rather than a place name: at least one digit and
    /// nothing but letters, digits, spaces and hyphens. Deliberately loose - a false positive
    /// costs one extra indexed lookup that finds nothing, and the name search still runs.
    /// </summary>
    public static bool LooksPostal(string value)
    {
        var hasDigit = false;
        foreach (var c in value)
        {
            if (char.IsAsciiDigit(c))
            {
                hasDigit = true;
            }
            else if (!char.IsAsciiLetter(c) && c != ' ' && c != '-')
            {
                return false;
            }
        }
        return hasDigit;
    }
}
