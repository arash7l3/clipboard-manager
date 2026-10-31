using System.Globalization;

namespace Clipo.Services;

/// <summary>The <see cref="AppLanguage"/> choices as language tags and cultures.</summary>
public static class UiLanguage
{
    /// <summary>
    /// The language tag of the resource folder for <paramref name="language"/>
    /// (<c>Strings\en-US</c>, <c>Strings\fa</c>); <c>null</c> follows Windows.
    /// </summary>
    public static string? Tag(AppLanguage language) => language switch
    {
        AppLanguage.English => "en-US",
        AppLanguage.Farsi => "fa",
        _ => null,
    };

    /// <summary>The culture for the user's language choice, or <c>null</c> to leave the one Windows gave the process.</summary>
    public static CultureInfo? Culture(AppLanguage language) =>
        Tag(language) is { } tag ? CultureInfo.GetCultureInfo(tag) : null;
}
