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
/// <param name="plainText">Loads an item's plain text by id, for pasting without formatting; <c>null</c> if it has none, such as an image. Disk I/O, so it runs off the UI thread.</param>
/// <param name="writeSnippet">Puts a snippet on the clipboard without telling the watcher to skip it, so the history records it like any copy. <paramref name="write"/> if not given.</param>
public sealed class ItemRestorer(
    Func<long, ClipItem?> load,
    Func<ItemContent, Task<bool>> write,
    Action<long> bump,
    Func<bool> autoPaste,
    Action paste,
    Func<long, string?>? plainText = null,
    Func<ItemContent, Task<bool>>? writeSnippet = null)
{
    /// <summary>Pause between giving the app underneath its focus back and pressing Ctrl+V.</summary>
    public static readonly TimeSpan PasteDelay = TimeSpan.FromMilliseconds(120);

    public TimeSpan Delay { get; init; } = PasteDelay;

    /// <summary>Restores the full item <paramref name="id"/>.</summary>
    public Task<RestoreOutcome> RestoreAsync(long id) => RestoreAsync(id, plain: false);

    /// <summary>
    /// Restores item <paramref name="id"/>, as its plain text if
    /// <paramref name="plain"/>: no formatting, files as their paths. An item
    /// without plain text, such as an image, is unavailable then.
    /// </summary>
    public async Task<RestoreOutcome> RestoreAsync(long id, bool plain)
    {
        ItemContent? content;
        if (plain)
        {
            var text = await Task.Run(() => LoadPlain(id)).ConfigureAwait(true);
            content = string.IsNullOrEmpty(text) ? null : new ItemContent.Text(new TextContent(text));
        }
        else
        {
            content = (await Task.Run(() => Load(id)).ConfigureAwait(true))?.Content;
        }
        if (content is null || !await write(content).ConfigureAwait(true))
        {
            return RestoreOutcome.Unavailable;
        }

        // The pick counts as a fresh copy: same item, same source, moved to the top.
        _ = Task.Run(() => Bump(id));

        return await FinishAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Puts a snippet's <paramref name="text"/> on the clipboard as plain
    /// text. The watcher records it like any copy, so it shows at the top of
    /// the history; the snippet itself stays as it is.
    /// </summary>
    public async Task<RestoreOutcome> RestoreTextAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0 || !await (writeSnippet ?? write)(new ItemContent.Text(new TextContent(text))).ConfigureAwait(true))
        {
            return RestoreOutcome.Unavailable;
        }
        return await FinishAsync().ConfigureAwait(true);
    }

    private async Task<RestoreOutcome> FinishAsync()
    {
        if (!autoPaste())
        {
            return RestoreOutcome.Restored;
        }
        await Task.Delay(Delay).ConfigureAwait(true);
        paste();
        return RestoreOutcome.Pasted;
    }

    private string? LoadPlain(long id)
    {
        try
        {
            return plainText?.Invoke(id);
        }
        catch (Exception e)
        {
            Trace.TraceError($"loading the plain text of item {id} failed: {e.GetType().Name}: {e.Message}");
            return null;
        }
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
