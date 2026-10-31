using System.Diagnostics;
using Clipo.Core;

namespace Clipo.Services;

/// <summary>What picking an item came to.</summary>
public enum RestoreOutcome
{
    /// <summary>On the clipboard; the user pastes it.</summary>
    Restored,

    /// <summary>On the clipboard, and Ctrl+V was sent to the app in front.</summary>
    Pasted,

    /// <summary>The item could not be loaded or the clipboard refused it; the clipboard is as it was or empty.</summary>
    Unavailable,
}

/// <summary>
/// Puts the item the user picked back on the clipboard and, if auto-paste is
/// on, pastes it into the app in front. Use it from the UI thread.
/// </summary>
/// <param name="load">Loads a full item by id; disk I/O, so it runs off the UI thread.</param>
/// <param name="write">Puts content on the clipboard; returns whether it worked. It also tells the watcher to skip Clipo's own write.</param>
/// <param name="bump">Moves an item to the top of the history, as if it was copied again. Runs off the UI thread.</param>
/// <param name="autoPaste">Whether to paste after restoring, read at each pick.</param>
/// <param name="paste">Sends Ctrl+V to the app in front.</param>
public sealed class ItemRestorer(
    Func<long, ClipItem?> load,
    Func<ItemContent, Task<bool>> write,
    Action<long> bump,
    Func<bool> autoPaste,
    Action paste)
{
    /// <summary>Pause between giving the app underneath its focus back and pressing Ctrl+V.</summary>
    public static readonly TimeSpan PasteDelay = TimeSpan.FromMilliseconds(120);

    public TimeSpan Delay { get; init; } = PasteDelay;

    /// <summary>Restores the full item <paramref name="id"/>.</summary>
    public async Task<RestoreOutcome> RestoreAsync(long id)
    {
        var item = await Task.Run(() => Load(id)).ConfigureAwait(true);
        if (item is null || !await write(item.Content).ConfigureAwait(true))
        {
            return RestoreOutcome.Unavailable;
        }

        // The pick counts as a fresh copy: same item, same source, moved to the top.
        _ = Task.Run(() => Bump(id));

        if (!autoPaste())
        {
            return RestoreOutcome.Restored;
        }
        await Task.Delay(Delay).ConfigureAwait(true);
        paste();
        return RestoreOutcome.Pasted;
    }

    private ClipItem? Load(long id)
    {
        try
        {
            return load(id);
        }
        catch (Exception e)
        {
            Trace.TraceError($"loading item {id} failed: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    private void Bump(long id)
    {
        try
        {
            bump(id);
        }
        catch (Exception e)
        {
            Trace.TraceWarning($"moving item {id} to the top failed: {e.GetType().Name}: {e.Message}");
        }
    }
}
