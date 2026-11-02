using System.Globalization;
using Clipo.Core;

namespace Clipo.Services;

/// <summary>
/// The settings an export carries, as key and value text. Keys without a
/// platform prefix mean the same on macOS, so a file from one imports on the
/// other; the shortcut and the ignored apps differ per platform and carry a
/// <c>windows.</c> prefix. Unknown keys and values a setting cannot take are
/// skipped on import.
/// </summary>
public static class SettingsTransfer
{
    public const string MaxItemsKey = "history.maxItems";
    public const string AutoClearDaysKey = "history.autoClearDays";
    public const string PrivateModeKey = "capture.privateMode";
    public const string AutoPasteKey = "paste.autoPaste";
    public const string AppearanceKey = "interface.appearance";
    public const string LanguageKey = "interface.language";
    public const string StartTabKey = "popup.startTab";
    public const string HotkeyKey = "windows.hotkey";
    public const string IgnoredAppsKey = "windows.ignoredApps";

    private static readonly Dictionary<AppTheme, string> s_themes = new()
    {
        [AppTheme.System] = "system",
        [AppTheme.Light] = "light",
        [AppTheme.Dark] = "dark",
    };

    private static readonly Dictionary<AppLanguage, string> s_languages = new()
    {
        [AppLanguage.System] = "system",
        [AppLanguage.English] = "en",
        [AppLanguage.Farsi] = "fa",
    };

    private static readonly Dictionary<StartTab, string> s_startTabs = new()
    {
        [StartTab.All] = "all",
        [StartTab.Text] = "text",
        [StartTab.Images] = "images",
        [StartTab.Files] = "files",
        [StartTab.Pinned] = "pinned",
        [StartTab.LastUsed] = "lastUsed",
    };

    /// <summary><paramref name="settings"/> as the pairs an export carries.</summary>
    public static IReadOnlyList<Setting> Export(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return
        [
            new(MaxItemsKey, settings.MaxItems.ToString(CultureInfo.InvariantCulture)),
            new(AutoClearDaysKey, settings.AutoClearDays.ToString(CultureInfo.InvariantCulture)),
            new(PrivateModeKey, Flag(settings.PrivateMode)),
            new(AutoPasteKey, Flag(settings.AutoPaste)),
            new(AppearanceKey, s_themes[settings.Theme]),
            new(LanguageKey, s_languages[settings.Language]),
            new(StartTabKey, s_startTabs[settings.StartTab]),
            new(HotkeyKey, settings.Hotkey.ToString()),
            new(IgnoredAppsKey, string.Join('\n', settings.IgnoredApps)),
        ];
    }

    /// <summary>
    /// <paramref name="current"/> with the values <paramref name="imported"/>
    /// holds. What it does not hold, or holds in a form this version cannot
    /// read, stays as it is.
    /// </summary>
    public static AppSettings Import(AppSettings current, IEnumerable<Setting> imported)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(imported);
        var next = current;
        foreach (var setting in imported)
        {
            next = With(next, setting.Key, setting.Value);
        }
        return next.Normalized();
    }

    private static AppSettings With(AppSettings settings, string key, string value) => key switch
    {
        MaxItemsKey when Number(value) is { } items => settings with { MaxItems = items },
        AutoClearDaysKey when Number(value) is { } days => settings with { AutoClearDays = days },
        PrivateModeKey when bool.TryParse(value, out var on) => settings with { PrivateMode = on },
        AutoPasteKey when bool.TryParse(value, out var on) => settings with { AutoPaste = on },
        AppearanceKey when Find(s_themes, value) is { } theme => settings with { Theme = theme },
        LanguageKey when Find(s_languages, value) is { } language => settings with { Language = language },
        StartTabKey when Find(s_startTabs, value) is { } tab => settings with { StartTab = tab },
        HotkeyKey when Enum.TryParse<HotkeyChoice>(value, out var hotkey) && Enum.IsDefined(hotkey) =>
            settings with { Hotkey = hotkey },
        IgnoredAppsKey => settings with { IgnoredApps = IgnoredAppList.Clean(value.Split('\n')) },
        _ => settings,
    };

    private static string Flag(bool on) => on ? "true" : "false";

    private static int? Number(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static T? Find<T>(Dictionary<T, string> names, string value)
        where T : struct, Enum
    {
        foreach (var (choice, name) in names)
        {
            if (name == value)
            {
                return choice;
            }
        }
        return null;
    }
}
