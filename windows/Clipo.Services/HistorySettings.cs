using System.Diagnostics;
using Clipo.Core;

namespace Clipo.Services;

/// <summary>Gives the open history the user's limits and rules.</summary>
public static class HistorySettings
{
    /// <summary>
    /// Sets the item limit and the capture rules from <paramref name="settings"/>
    /// and deletes what has expired as of <paramref name="now"/>. The core
    /// keeps limits and rules in memory only, so this runs every time the
    /// history is opened as well as when a setting changes. Returns whether
    /// it worked. Does disk I/O: call it off the UI thread.
    /// </summary>
    public static bool Apply(ClipStore store, AppSettings settings, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(settings);
        try
        {
            store.SetLimits(store.Limits() with { MaxItems = (ulong)settings.MaxItems });
            store.SetRules(CaptureSettings.Rules(settings));
            store.ClearExpired(now);
            return true;
        }
        catch (Exception e)
        {
            // Core errors carry codes and ids, never clipboard content.
            Trace.TraceError($"applying the history settings failed: {e.GetType().Name}: {e.Message}");
            return false;
        }
    }
}
