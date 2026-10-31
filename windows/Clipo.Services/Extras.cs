using System.Diagnostics;
using Clipo.Core;

namespace Clipo.Services;

/// <summary>How importing a file came to.</summary>
public enum ImportOutcome
{
    Imported,

    /// <summary>Not a Clipo export, or damaged.</summary>
    Invalid,

    /// <summary>Written by a newer Clipo.</summary>
    TooNew,

    /// <summary>The history is closed or the store failed.</summary>
    Failed,
}

/// <param name="Outcome">What happened.</param>
/// <param name="Summary">What was added and skipped; set only when <paramref name="Outcome"/> is <see cref="ImportOutcome.Imported"/>.</param>
public sealed record ImportResult(ImportOutcome Outcome, ImportSummary? Summary = null);

/// <summary>
/// Snippets and export / import on top of the open history. Every call does
/// disk I/O and runs off the calling thread. Nothing here logs snippet text
/// or exported content.
/// </summary>
/// <param name="store">The open history, or <c>null</c> while there is none. Read at each call, as the history can be replaced.</param>
public sealed class Extras(Func<ClipStore?> store)
{
    /// <summary>Longest snippet name in a menu, in characters.</summary>
    public const int MaxMenuTitleLength = 40;

    /// <summary>The saved snippets in creation order; <c>null</c> if the history is closed or the store failed.</summary>
    public Task<IReadOnlyList<Snippet>?> SnippetsAsync() => Run<IReadOnlyList<Snippet>?>(
        "listing snippets", s => s.Snippets(""), null);

    /// <summary>
    /// Adds a snippet, or changes the one with <paramref name="id"/>. Returns
    /// false if the name is blank, the text is empty or the store failed.
    /// </summary>
    public Task<bool> SaveSnippetAsync(long? id, string title, string text) => Run("saving a snippet", s =>
    {
        if (id is { } existing)
        {
            s.UpdateSnippet(existing, title, text, DateTime.UtcNow);
        }
        else
        {
            s.AddSnippet(title, text, DateTime.UtcNow);
        }
        return true;
    }, false);

    public Task<bool> DeleteSnippetAsync(long id) => Run("deleting a snippet", s =>
    {
        s.DeleteSnippet(id);
        return true;
    }, false);

    /// <summary>The history and snippets as one blob, not encrypted; <c>null</c> on failure.</summary>
    public Task<byte[]?> ExportAsync() => Run<byte[]?>("exporting", s => s.ExportData(), null);

    /// <summary>Adds what <paramref name="data"/> holds. On any failure nothing was imported.</summary>
    public Task<ImportResult> ImportAsync(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Task.Run(() =>
        {
            try
            {
                return store() is { } s
                    ? new ImportResult(ImportOutcome.Imported, s.ImportData(data))
                    : new ImportResult(ImportOutcome.Failed);
            }
            catch (ClipoException.InvalidExport)
            {
                return new ImportResult(ImportOutcome.Invalid);
            }
            catch (ClipoException.UnsupportedExport)
            {
                return new ImportResult(ImportOutcome.TooNew);
            }
            catch (Exception e)
            {
                Trace.TraceError($"importing failed: {e.GetType().Name}");
                return new ImportResult(ImportOutcome.Failed);
            }
        });
    }

    /// <summary>
    /// A snippet's name as a menu line: cut to <see cref="MaxMenuTitleLength"/>
    /// characters, on one line, with each <c>&amp;</c> doubled so the menu does
    /// not read it as a mnemonic.
    /// </summary>
    public static string MenuTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var line = string.Join(' ', title.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (line.Length > MaxMenuTitleLength)
        {
            // Not through a surrogate pair.
            var cut = char.IsHighSurrogate(line[MaxMenuTitleLength - 1]) ? MaxMenuTitleLength - 1 : MaxMenuTitleLength;
            line = line[..cut] + "…";
        }
        return line.Replace("&", "&&", StringComparison.Ordinal);
    }

    private Task<T> Run<T>(string what, Func<ClipStore, T> action, T failed) => Task.Run(() =>
    {
        try
        {
            return store() is { } s ? action(s) : failed;
        }
        catch (Exception e)
        {
            // Core errors carry codes, never clipboard content.
            Trace.TraceError($"{what} failed: {e.GetType().Name}");
            return failed;
        }
    });
}
