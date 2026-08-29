using TrmnlApi.Geo;

namespace TrmnlApi.Tests;

public class CountryPreferenceTests
{
    [Theory]
    [InlineData("US", "US")]
    [InlineData("us", "US")]
    [InlineData("  us  ", "US")]
    // The plugin dropdown's own option value, which is what actually arrived once the Liquid
    // filter meant to trim it did not.
    [InlineData("US - United States of America", "US")]
    [InlineData("GB - United Kingdom", "GB")]
    [InlineData("FR - France", "FR")]
    public void Parse_UsableForms_ReturnTheCode(string input, string expected)
        => Assert.Equal(expected, CountryPreference.Parse(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    // The dropdown's default. Three letters, so it is a word rather than a code with a label.
    [InlineData("Auto")]
    [InlineData("United States")]
    [InlineData("U")]
    [InlineData("1")]
    [InlineData("12")]
    public void Parse_UnusableForms_ReturnNull(string? input)
        => Assert.Null(CountryPreference.Parse(input));
}
