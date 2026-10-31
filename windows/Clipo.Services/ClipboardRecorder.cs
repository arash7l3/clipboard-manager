using System.Diagnostics;
using Clipo.Core;

namespace Clipo.Services;

/// <summary>Turns a clipboard change into a stored history item.</summary>
public sealed class ClipboardRecorder(CaptureBuilder builder)
{
    /// <summary>
    /// Records <paramref name="change"/> in <paramref name="store"/>. Returns
    /// what the store did, or <c>null</c> if the change was not worth keeping
    /// or storing failed. Does image decoding and disk I/O: call it off the UI thread.
    /// </summary>
    public async Task<Recorded?> RecordAsync(ClipboardChange change, ClipStore store)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(store);
        try
        {
            var limits = store.Limits();
            var capture = await builder.BuildAsync(change, (long)Math.Min(limits.MaxItemBytes, long.MaxValue)).ConfigureAwait(false);
            return capture is null ? null : store.Record(capture);
        }
        catch (Exception e)
        {
            // Core errors carry codes and ids, never clipboard content.
            Trace.TraceError($"recording a capture failed: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }
}
