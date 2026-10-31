using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Clipo.Tests;

/// <summary>Checks the app's English and Farsi string tables against each other.</summary>
public sealed partial class LocalizationTests
{
    private static readonly string s_strings = Path.Combine(AppContext.BaseDirectory, "Strings");

    [Fact]
    public void EveryLanguageHasTheSameKeys()
    {
        var english = Load("en-US");
        var farsi = Load("fa");

        Assert.NotEmpty(english);
        Assert.Equal(english.Keys.Order(), farsi.Keys.Order());
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fa")]
    public void NoStringIsEmpty(string language)
    {
        foreach (var (key, value) in Load(language))
        {
            Assert.False(string.IsNullOrWhiteSpace(value), $"{language}: {key} is empty");
        }
    }

    [Fact]
    public void TranslationsKeepTheFormatPlaceholders()
    {
        var farsi = Load("fa");
        foreach (var (key, value) in Load("en-US"))
        {
            Assert.Equal(Placeholders(value), Placeholders(farsi[key]));
        }
    }

    private static Dictionary<string, string> Load(string language) =>
        XDocument.Load(Path.Combine(s_strings, language, "Resources.resw"))
            .Root!
            .Elements("data")
            .ToDictionary(
                data => (string)data.Attribute("name")!,
                data => (string?)data.Element("value") ?? "");

    private static string[] Placeholders(string value) =>
        [.. PlaceholderPattern().Matches(value).Select(match => match.Value).Order()];

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderPattern();
}
