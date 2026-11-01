using System.Globalization;
using Clipo.Core;

namespace Clipo.Services;

/// <summary>The words the popup shows for rows, from the app's string table.</summary>
/// <param name="strings">Looks a string up by its key in <c>Resources.resw</c>.</param>
/// <param name="culture">Formats numbers and dates.</param>
public sealed class PopupText(Func<string, string> strings, CultureInfo culture)
{
    /// <summary>Longest row title, in characters. The row cuts it to its width anyway; this only keeps the text short.</summary>
    public const int MaxTitleLength = 300;

    /// <summary>The row's title: the name the user gave the item, or else a line describing its content.</summary>
    public string Title(ItemPreview row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.Title ?? Title(row.Content);
    }

    /// <summary>The tooltip and name of a row's rename button.</summary>
    public string RenameItem => strings("RenameItem");

    /// <summary>The tooltip and name of a row's delete button.</summary>
    public string DeleteItem => strings("DeleteItem");

    /// <summary>The placeholder of the field that shows a row's new name.</summary>
    public string ItemNameHint => strings("ItemNameHint");

    /// <summary>One line describing the content: the first non-blank line of a text, an image's size, or the first file's name.</summary>
    public string Title(PreviewContent content) => content switch
    {
        PreviewContent.Text text => FirstLine(text.Snippet),
        PreviewContent.Image image => Format("ImageTitle", image.Width, image.Height),
        PreviewContent.Files files => FilesTitle(files),
        _ => "",
    };

    /// <summary>Where and when the item was copied, for the row's second line.</summary>
    public string Detail(ItemPreview row, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(row);
        var time = CopiedTime(row.LastCopiedAt, now);
        return row.SourceApp is { } app ? Format("RowDetail", app.Name ?? app.Id, time) : time;
    }

    /// <summary>The kind of content, as a screen reader says it.</summary>
    public string Kind(PreviewContent content) => content switch
    {
        PreviewContent.Text { Hint: TextHint.Link } => strings("KindLink"),
        PreviewContent.Text { Hint: TextHint.Code } => strings("KindCode"),
        PreviewContent.Text => strings("KindText"),
        PreviewContent.Image => strings("KindImage"),
        PreviewContent.Files => strings("KindFiles"),
        _ => "",
    };

    /// <summary>Everything a row shows, in one sentence for screen readers.</summary>
    public string AccessibleName(ItemPreview row, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(row);
        List<string> parts = [Kind(row.Content), Title(row), Detail(row, now)];
        if (row.Pinned)
        {
            parts.Add(strings("PinnedNote"));
        }
        return string.Join(strings("LabelSeparator"), parts.Where(part => part.Length > 0));
    }

    /// <summary>The quick-paste key of the <paramref name="number"/>th row, such as "Ctrl+1".</summary>
    public string Shortcut(int number) => Format("QuickPasteKey", number);

    /// <summary>The Clear button's tooltip and name for <paramref name="category"/>, such as "Clear Images (pinned items are kept)".</summary>
    public string ClearTab(Category category) => strings(category switch
    {
        Category.Text => "ClearText",
        Category.Images => "ClearImages",
        Category.Files => "ClearFiles",
        _ => "ClearAll",
    });

    /// <summary>What the list says when it has no rows.</summary>
    public string EmptyList(bool failed, bool searching) =>
        strings(failed ? "StatusUnavailable" : searching ? "NoMatches" : "NoItems");

    /// <summary>"now" within the last minute, then minutes, hours and days ago, then the date.</summary>
    public string CopiedTime(DateTime copied, DateTime now)
    {
        var age = now.ToUniversalTime() - copied.ToUniversalTime();
        return age switch
        {
            _ when age < TimeSpan.FromMinutes(1) => strings("TimeNow"),
            _ when age < TimeSpan.FromHours(1) => Format("TimeMinutes", (int)age.TotalMinutes),
            _ when age < TimeSpan.FromDays(1) => Format("TimeHours", (int)age.TotalHours),
            _ when age < TimeSpan.FromDays(7) => Format("TimeDays", (int)age.TotalDays),
            _ => copied.ToLocalTime().ToString("d", culture),
        };
    }

    private string FilesTitle(PreviewContent.Files files)
    {
        var name = files.FilesValue.Length > 0 ? FileName(files.FilesValue[0].Path) : "";
        return files.Total > 1 ? Format("FilesTitle", name, files.Total - 1) : name;
    }

    private static string FirstLine(string snippet)
    {
        var line = snippet.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return line.Length > MaxTitleLength ? line[..MaxTitleLength] : line;
    }

    private static string FileName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/').Replace('\\', Path.DirectorySeparatorChar));
        return name.Length > 0 ? name : path;
    }

    private string Format(string key, params object[] values) => string.Format(culture, strings(key), values);
}
