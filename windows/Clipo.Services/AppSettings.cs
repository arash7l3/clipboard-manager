using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clipo.Services;

/// <summary>Light, dark, or whatever Windows uses.</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>The language of Clipo's own text.</summary>
public enum AppLanguage
{
    System,
    English,
    Farsi,
}

/// <summary>
/// The shortcut that opens the popup: always the V key, with one of a few
/// modifier sets. A fixed list cannot clash with itself or ask for a key
/// that does not exist on a layout.
/// </summary>
public enum HotkeyChoice
{
    ControlShift,
    ControlAlt,
    AltShift,
    ControlAltShift,
}

/// <summary>The user's choices. Missing values keep their defaults.</summary>
public sealed record AppSettings
{
    /// <summary>Decision 0004; a test keeps it equal to the core's default.</summary>
    public const int DefaultMaxItems = 200;

    /// <summary>The numbers of items Settings offers.</summary>
    public static IReadOnlyList<int> MaxItemsChoices { get; } = [50, 100, 200, 500, 1000];

    /// <summary>The ages, in days, after which Settings can remove unpinned items; 0 keeps them.</summary>
    public static IReadOnlyList<int> AutoClearDaysChoices { get; } = [0, 1, 7, 30];

    private const int LargestMaxItems = 100_000;

    /// <summary>
    /// Whether picking an item also pastes it into the app in front. Off by
    /// default: it can paste into the wrong place (decision 0004).
    /// </summary>
    public bool AutoPaste { get; init; }

    public AppTheme Theme { get; init; }

    public AppLanguage Language { get; init; }

    public HotkeyChoice Hotkey { get; init; }

    /// <summary>
    /// The most unpinned items kept. Not <c>init</c>: the JSON source
    /// generator gives a missing init-only property the type's default (0)
    /// instead of its initializer.
    /// </summary>
    public int MaxItems { get; set; } = DefaultMaxItems;

    /// <summary>Days after which unpinned items are deleted; 0 keeps them.</summary>
    public int AutoClearDays { get; init; }

    /// <summary>While on, nothing that is copied is stored.</summary>
    public bool PrivateMode { get; init; }

    /// <summary>Executable names the user added to the built-in list of apps that are never recorded.</summary>
    public IReadOnlyList<string> IgnoredApps { get; init; } = [];

    /// <summary>How long unpinned items stay, or <c>null</c> for ever.</summary>
    [JsonIgnore]
    public TimeSpan? AutoClearAfter => AutoClearDays > 0 ? TimeSpan.FromDays(AutoClearDays) : null;

    /// <summary>These settings with values a hand-edited file could hold brought back into range.</summary>
    public AppSettings Normalized() => this with
    {
        Theme = Enum.IsDefined(Theme) ? Theme : AppTheme.System,
        Language = Enum.IsDefined(Language) ? Language : AppLanguage.System,
        Hotkey = Enum.IsDefined(Hotkey) ? Hotkey : HotkeyChoice.ControlShift,
        MaxItems = Math.Clamp(MaxItems, 1, LargestMaxItems),
        AutoClearDays = Math.Clamp(AutoClearDays, 0, 3650),
        IgnoredApps = IgnoredAppList.Clean(IgnoredApps ?? []),
    };

    /// <summary>
    /// Whether <paramref name="other"/> keeps the same items under the same
    /// rules: item limit, auto-clear, private mode and ignored apps. Only a
    /// difference here needs the open history to be updated.
    /// </summary>
    public bool HasSameRetentionAs(AppSettings other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return MaxItems == other.MaxItems
            && AutoClearDays == other.AutoClearDays
            && PrivateMode == other.PrivateMode
            && IgnoredApps.SequenceEqual(other.IgnoredApps, StringComparer.Ordinal);
    }

    /// <summary>Records compare lists by reference; settings compare their lists by content.</summary>
    public bool Equals(AppSettings? other) =>
        other is not null
        && AutoPaste == other.AutoPaste
        && Theme == other.Theme
        && Language == other.Language
        && Hotkey == other.Hotkey
        && MaxItems == other.MaxItems
        && AutoClearDays == other.AutoClearDays
        && PrivateMode == other.PrivateMode
        && IgnoredApps.SequenceEqual(other.IgnoredApps, StringComparer.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(AutoPaste);
        hash.Add(Theme);
        hash.Add(Language);
        hash.Add(Hotkey);
        hash.Add(MaxItems);
        hash.Add(AutoClearDays);
        hash.Add(PrivateMode);
        foreach (var app in IgnoredApps)
        {
            hash.Add(app, StringComparer.Ordinal);
        }
        return hash.ToHashCode();
    }
}

/// <summary>Keeps <see cref="AppSettings"/> in a small JSON file next to the history.</summary>
/// <param name="path">The settings file.</param>
public sealed class AppSettingsStore(string path)
{
    /// <summary><c>Clipo\settings.json</c> in the user's local app data, beside the history.</summary>
    public static string DefaultPath(string? localAppData = null) =>
        Path.Combine(Path.GetDirectoryName(History.DatabasePath(localAppData))!, "settings.json");

    /// <summary>The saved settings, or the defaults if there are none or the file is unreadable.</summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(path))
            {
                return new AppSettings();
            }
            using var file = File.OpenRead(path);
            return JsonSerializer.Deserialize(file, SettingsJson.Default.AppSettings)?.Normalized() ?? new AppSettings();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Trace.TraceWarning($"reading the settings failed, using defaults: {e.GetType().Name}");
            return new AppSettings();
        }
    }

    /// <summary>Saves <paramref name="settings"/>, replacing the file in one step. Returns whether it worked.</summary>
    public bool Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(settings, SettingsJson.Default.AppSettings));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Trace.TraceError($"saving the settings failed: {e.GetType().Name}");
            return false;
        }
    }
}

/// <summary>The apps the user asked never to record, kept as executable file names.</summary>
public static class IgnoredAppList
{
    /// <summary>
    /// The file name for what the user typed or picked: any folder is dropped
    /// and <c>.exe</c> added if there is no extension, so <c>keepass</c> and
    /// <c>C:\Tools\KeePass.exe</c> both become <c>KeePass.exe</c>. <c>null</c> if nothing is left.
    /// </summary>
    public static string? Normalize(string? text)
    {
        var trimmed = text?.Trim().Trim('"');
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }
        var name = trimmed[(trimmed.LastIndexOfAny(['\\', '/']) + 1)..];
        if (name.Length == 0 || name.Any(char.IsControl))
        {
            return null;
        }
        return Path.HasExtension(name) ? name : name + ".exe";
    }

    /// <summary>
    /// <paramref name="apps"/> normalized, without blanks, repeats (ignoring
    /// case) or apps the built-in list already has.
    /// </summary>
    public static IReadOnlyList<string> Clean(IEnumerable<string?> apps)
    {
        var seen = new HashSet<string>(CaptureSettings.DefaultIgnoredApps, StringComparer.OrdinalIgnoreCase);
        return [.. apps.Select(Normalize).OfType<string>().Where(seen.Add)];
    }

    /// <summary><paramref name="apps"/> with <paramref name="text"/> added, or the same list if it adds nothing.</summary>
    public static IReadOnlyList<string> Add(IReadOnlyList<string> apps, string? text) =>
        Normalize(text) is { } name ? Clean([.. apps, name]) : apps;

    /// <summary><paramref name="apps"/> without <paramref name="name"/> (ignoring case).</summary>
    public static IReadOnlyList<string> Remove(IReadOnlyList<string> apps, string name) =>
        [.. apps.Where(app => !string.Equals(app, name, StringComparison.OrdinalIgnoreCase))];
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
