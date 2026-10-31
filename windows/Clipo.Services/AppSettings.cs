using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clipo.Services;

/// <summary>The user's choices. Missing values keep their defaults.</summary>
public sealed record AppSettings
{
    /// <summary>
    /// Whether picking an item also pastes it into the app in front. Off by
    /// default: it can paste into the wrong place (decision 0004).
    /// </summary>
    public bool AutoPaste { get; init; }
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
            return JsonSerializer.Deserialize(file, SettingsJson.Default.AppSettings) ?? new AppSettings();
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

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
