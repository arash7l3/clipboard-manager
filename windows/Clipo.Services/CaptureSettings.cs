using Clipo.Core;

namespace Clipo.Services;

/// <summary>
/// Which captures are refused. The core keeps these rules in memory only, so
/// the app sets them every time it opens the history.
/// </summary>
public static class CaptureSettings
{
    /// <summary>
    /// Password managers and the like, ignored out of the box, by executable
    /// name. Most of them also mark what they copy as concealed; this covers
    /// the ones that don't. Matched ignoring case.
    /// </summary>
    public static IReadOnlyList<string> DefaultIgnoredApps { get; } =
    [
        "1Password.exe",
        "Bitwarden.exe",
        "KeePass.exe",
        "KeePassXC.exe",
        "Dashlane.exe",
        "Enpass.exe",
        "ProtonPass.exe",
        "NordPass.exe",
        "RoboForm.exe",
    ];

    /// <summary>The rules to give the core: capture on, the built-in ignore list, nothing auto-cleared.</summary>
    public static CaptureRules Rules(bool privateMode = false) =>
        new(PrivateMode: privateMode, IgnoredApps: [.. DefaultIgnoredApps], AutoClearAfter: null);

    /// <summary>
    /// Whether a copy made in <paramref name="app"/> may be read at all under
    /// <paramref name="rules"/>: the check made before touching the
    /// clipboard's data. The core applies the same rules again when a capture
    /// is recorded.
    /// </summary>
    public static bool AllowsReading(SourceApp? app, CaptureRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.PrivateMode)
        {
            return false;
        }
        return app is null || !(rules.IgnoredApps ?? []).Contains(app.Id, StringComparer.OrdinalIgnoreCase);
    }
}
