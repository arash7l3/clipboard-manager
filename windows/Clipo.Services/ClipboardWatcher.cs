using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading.Channels;
using Clipo.Core;
using Clipo.Services.Native;

namespace Clipo.Services;

/// <summary>What the watcher kept: a new item, or an old one moved to the top.</summary>
public sealed class ClipboardRecordedEventArgs(Recorded result) : EventArgs
{
    public Recorded Result { get; } = result;
}

/// <summary>
/// Notices clipboard changes and records them in the history.
///
/// Windows sends <c>WM_CLIPBOARDUPDATE</c> to a registered window on every
/// change, so nothing is polled. The source app is found right away on the UI
/// thread; reading, decoding and the database write happen on a background
/// worker, one change at a time so the history keeps copy order. Whatever is
/// on the clipboard when the watcher starts is not captured: it may come from
/// an ignored app, and only a new copy tells us which.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ClipboardWatcher : IDisposable
{
    private readonly MessageWindow _window;
    private readonly ClipboardRecorder _recorder;
    private readonly Func<CaptureRules> _rules;
    private readonly Channel<PendingChange> _pending = Channel.CreateBounded<PendingChange>(
        new BoundedChannelOptions(16) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private ClipStore? _store;
    private uint _lastSequence;
    private bool _listening;

    /// <param name="window">Receives the clipboard's change messages.</param>
    /// <param name="recorder">Builds captures and stores them.</param>
    /// <param name="rules">The rules in force; asked before any data is read, so private mode and ignored apps drop a change unread.</param>
    public ClipboardWatcher(MessageWindow window, ClipboardRecorder recorder, Func<CaptureRules> rules)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(rules);
        _window = window;
        _recorder = recorder;
        _rules = rules;
    }

    /// <summary>Raised on a background thread after a change was stored.</summary>
    public event EventHandler<ClipboardRecordedEventArgs>? Recorded;

    /// <summary>
    /// Where changes are stored. While it is <c>null</c>, for example when
    /// the history is not open, changes are dropped unread. The caller owns
    /// the store and sets this to <c>null</c> before disposing it.
    /// </summary>
    public ClipStore? Store
    {
        get => Volatile.Read(ref _store);
        set => Volatile.Write(ref _store, value);
    }

    /// <summary>Starts listening. Returns false if Windows refused.</summary>
    public bool Start()
    {
        if (_listening)
        {
            return true;
        }
        // Windows posts an update when a listener is added; it is not a copy.
        _lastSequence = NativeMethods.GetClipboardSequenceNumber();
        if (!NativeMethods.AddClipboardFormatListener(_window.Handle))
        {
            Trace.TraceError("registering the clipboard listener failed");
            return false;
        }
        _listening = true;
        _window.MessageReceived += OnMessage;
        _ = Task.Run(() => RunAsync(_stop.Token));
        return true;
    }

    /// <summary>
    /// Treats what is on the clipboard now as seen. Call it right after Clipo
    /// itself wrote an item back, which must not count as a new copy.
    /// </summary>
    public void SkipCurrent() => _lastSequence = NativeMethods.GetClipboardSequenceNumber();

    public void Dispose()
    {
        if (_listening)
        {
            _window.MessageReceived -= OnMessage;
            NativeMethods.RemoveClipboardFormatListener(_window.Handle);
            _listening = false;
        }
        _pending.Writer.TryComplete();
        _stop.Cancel();
        _stop.Dispose();
    }

    private void OnMessage(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message != NativeMethods.WmClipboardUpdate)
        {
            return;
        }
        // One copy often causes several updates; they share a sequence number.
        var sequence = NativeMethods.GetClipboardSequenceNumber();
        if (sequence == _lastSequence)
        {
            return;
        }
        _lastSequence = sequence;
        _pending.Writer.TryWrite(new PendingChange(SourceAppResolver.Current(), DateTime.UtcNow));
    }

    private async Task RunAsync(CancellationToken stop)
    {
        try
        {
            await foreach (var change in _pending.Reader.ReadAllAsync(stop).ConfigureAwait(false))
            {
                await ProcessAsync(change).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The watcher was disposed.
        }
    }

    private async Task ProcessAsync(PendingChange change)
    {
        try
        {
            var store = Store;
            if (store is null || !CaptureSettings.AllowsReading(change.App, _rules()))
            {
                return;
            }
            // Earlier changes may have kept the worker busy, and the clipboard
            // may have changed again meanwhile. Its data then belongs to the
            // app that owns it now, which has to be allowed as well.
            var content = ClipboardReader.Read(
                (long)Math.Min(store.Limits().MaxItemBytes, long.MaxValue),
                () => CaptureSettings.AllowsReading(SourceAppResolver.CurrentWithoutName(), _rules()));
            if (content is null)
            {
                return;
            }
            var result = await _recorder.RecordAsync(new ClipboardChange(content, change.App, change.Date), store).ConfigureAwait(false);
            if (result is Core.Recorded.Added or Core.Recorded.Bumped)
            {
                Recorded?.Invoke(this, new ClipboardRecordedEventArgs(result));
            }
        }
        catch (Exception e)
        {
            // The history may have been closed meanwhile. Never log clipboard content.
            Trace.TraceError($"handling a clipboard change failed: {e.GetType().Name}: {e.Message}");
        }
    }

    private readonly record struct PendingChange(SourceApp? App, DateTime Date);
}
