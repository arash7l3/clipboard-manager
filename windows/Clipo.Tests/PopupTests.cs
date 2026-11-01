using System.Globalization;
using System.Xml.Linq;
using Clipo.Core;
using Clipo.Services;

namespace Clipo.Tests;

public sealed class PopupModelTests
{
    private static readonly DateTime s_date = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ResetClearsTheRowsAtOnceThenLoadsTheAllTab()
    {
        var loader = new FakeLoader { Rows = Rows(1, 2) };
        var model = new PopupModel(loader.Load, TimeSpan.Zero);
        await model.SetCategoryAsync(Category.Images);
        await model.SetSearchTextAsync("old");

        // Held back, so the answer cannot arrive (on another thread) before the assertions.
        loader.Gate = new ManualResetEventSlim();
        var task = model.ResetAsync();

        Assert.Empty(model.Rows);
        Assert.Null(model.Selection);
        loader.Gate.Set();
        await task;
        Assert.Equal(new Query(Category.All, ""), loader.Queries[^1]);
        Assert.Equal([1L, 2L], model.Rows.Select(row => row.Id));
        Assert.Equal(0, model.Selection);
        Assert.False(model.IsLoading);
    }

    [Fact]
    public async Task QuickPasteKeysPickTheFirstNineRows()
    {
        var model = new PopupModel(new FakeLoader { Rows = Rows(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11) }.Load, TimeSpan.Zero);
        await model.ResetAsync();

        Assert.Equal(1, model.RowForShortcut(1)?.Id);
        Assert.Equal(9, model.RowForShortcut(9)?.Id);
        Assert.Null(model.RowForShortcut(0));
        Assert.Null(model.RowForShortcut(10));
    }

    [Fact]
    public async Task QuickPasteKeyWithoutARowPicksNothing()
    {
        var model = new PopupModel(new FakeLoader { Rows = Rows(1, 2) }.Load, TimeSpan.Zero);
        await model.ResetAsync();

        Assert.NotNull(model.RowForShortcut(2));
        Assert.Null(model.RowForShortcut(3));
    }

    [Fact]
    public async Task ResetCanStartOnAnotherTab()
    {
        var loader = new FakeLoader { Rows = Rows(1) };
        var model = new PopupModel(loader.Load, TimeSpan.Zero);

        await model.ResetAsync(Category.Pinned);

        Assert.Equal(Category.Pinned, model.Category);
        Assert.Equal(new Query(Category.Pinned, ""), loader.Queries[^1]);
    }

    [Fact]
    public async Task ResetOnTheLastTabKeepsIt()
    {
        var model = new PopupModel(new FakeLoader { Rows = Rows(1) }.Load, TimeSpan.Zero);
        await model.SetCategoryAsync(Category.Files);

        await model.ResetAsync(model.Category);

        Assert.Equal(Category.Files, model.Category);
    }

    [Fact]
    public async Task EmptyAnswerSelectsNothing()
    {
        var model = new PopupModel(new FakeLoader().Load, TimeSpan.Zero);

        await model.ResetAsync();

        Assert.Null(model.Selection);
        Assert.Null(model.SelectedRow);
        Assert.False(model.Failed);
    }

    [Fact]
    public async Task FailedLoadIsReported()
    {
        var model = new PopupModel(_ => throw new InvalidOperationException("store closed"), TimeSpan.Zero);

        await model.ResetAsync();

        Assert.True(model.Failed);
        Assert.Empty(model.Rows);
    }

    [Fact]
    public async Task OnlyTheLatestAnswerIsShown()
    {
        using var slow = new ManualResetEventSlim();
        var model = new PopupModel(query =>
        {
            if (query.Text == "first")
            {
                slow.Wait();
                return Rows(1);
            }
            return Rows(2);
        }, TimeSpan.Zero);

        var first = model.SetSearchTextAsync("first");
        await model.SetSearchTextAsync("second");
        slow.Set();
        await first;

        Assert.Equal([2L], model.Rows.Select(row => row.Id));
    }

    [Fact]
    public async Task TypingQuicklySearchesOnlyForTheLastText()
    {
        var loader = new FakeLoader { Rows = Rows(1) };
        var model = new PopupModel(loader.Load, TimeSpan.FromMilliseconds(50));

        var tasks = new[] { model.SetSearchTextAsync("h"), model.SetSearchTextAsync("he"), model.SetSearchTextAsync("hey") };
        await Task.WhenAll(tasks);

        Assert.Equal(["hey"], loader.Queries.Select(query => query.Text));
    }

    [Fact]
    public async Task SameSearchTextDoesNotLoadAgain()
    {
        var loader = new FakeLoader();
        var model = new PopupModel(loader.Load, TimeSpan.Zero);

        await model.SetSearchTextAsync("x");
        await model.SetSearchTextAsync("x");

        Assert.Single(loader.Queries);
    }

    [Fact]
    public async Task EveryTabButPinnedCanBeCleared()
    {
        var model = new PopupModel(new FakeLoader().Load, TimeSpan.Zero);

        foreach (var category in PopupModel.Categories)
        {
            await model.SetCategoryAsync(category);
            Assert.Equal(category != Category.Pinned, model.CanClear);
        }
    }

    [Fact]
    public async Task RefreshKeepsTheSelectedItem()
    {
        var loader = new FakeLoader { Rows = Rows(1, 2, 3) };
        var model = new PopupModel(loader.Load, TimeSpan.Zero);
        await model.ResetAsync();
        model.Select(1);

        loader.Rows = Rows(9, 1, 2, 3);
        await model.RefreshAsync();

        Assert.Equal(2L, model.SelectedRow?.Id);
    }

    [Fact]
    public async Task RefreshSelectsTheFirstRowWhenTheSelectedItemIsGone()
    {
        var loader = new FakeLoader { Rows = Rows(1, 2) };
        var model = new PopupModel(loader.Load, TimeSpan.Zero);
        await model.ResetAsync();
        model.Select(1);

        loader.Rows = Rows(7, 1);
        await model.RefreshAsync();

        Assert.Equal(0, model.Selection);
    }

    [Fact]
    public async Task SelectionStopsAtTheEnds()
    {
        var model = new PopupModel(new FakeLoader { Rows = Rows(1, 2, 3) }.Load, TimeSpan.Zero);
        await model.ResetAsync();
        var moves = 0;
        model.SelectionChanged += (_, _) => moves++;

        Assert.False(model.MoveSelection(-1));
        Assert.True(model.MoveSelection(10));
        Assert.Equal(2, model.Selection);
        Assert.False(model.MoveSelection(1));
        Assert.True(model.MoveSelection(-1));
        Assert.Equal(1, model.Selection);
        Assert.Equal(2, moves);
    }

    [Fact]
    public async Task SelectRejectsRowsThatDoNotExist()
    {
        var model = new PopupModel(new FakeLoader { Rows = Rows(1) }.Load, TimeSpan.Zero);
        await model.ResetAsync();

        Assert.False(model.Select(5));
        Assert.False(model.Select(-1));
        Assert.False(model.Select(0));
    }

    [Fact]
    public async Task TabsWrapAround()
    {
        var model = new PopupModel(new FakeLoader().Load, TimeSpan.Zero);

        await model.MoveCategoryAsync(-1);
        Assert.Equal(Category.Pinned, model.Category);
        await model.MoveCategoryAsync(1);
        Assert.Equal(Category.All, model.Category);
        await model.MoveCategoryAsync(2);
        Assert.Equal(Category.Images, model.Category);
    }

    private static ItemPreview[] Rows(params long[] ids) =>
        [.. ids.Select(id => new ItemPreview(id, new PreviewContent.Text($"item {id}", false, TextHint.Plain), false, null, s_date, null))];

    private sealed class FakeLoader
    {
        private readonly Lock _lock = new();

        public ItemPreview[] Rows { get; set; } = [];

        public List<Query> Queries { get; } = [];

        /// <summary>When set, <see cref="Load"/> waits until it is.</summary>
        public ManualResetEventSlim? Gate { get; set; }

        public ItemPreview[]? Load(Query query)
        {
            Gate?.Wait();
            lock (_lock)
            {
                Queries.Add(query);
                return Rows;
            }
        }
    }
}

public sealed class PopupTextTests
{
    private static readonly DateTime s_now = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
    private static readonly PopupText s_text = Text("en-US", "en-US");

    [Fact]
    public void TextTitleIsTheFirstLineWithContent()
    {
        Assert.Equal("hello", s_text.Title(Snippet("\r\n   \n  hello  \nworld")));
    }

    [Fact]
    public void LongTitlesAreCut()
    {
        Assert.Equal(PopupText.MaxTitleLength, s_text.Title(Snippet(new string('x', 1000))).Length);
    }

    [Fact]
    public void ImageTitleGivesItsSize()
    {
        Assert.Equal("Image 640 × 480", s_text.Title(new PreviewContent.Image(ImageFormat.Png, 640, 480, null)));
    }

    [Fact]
    public void FilesTitleGivesTheFirstNameAndHowManyMore()
    {
        var one = new PreviewContent.Files([new FileRef(@"C:\Users\me\report.pdf")], 1);
        var three = new PreviewContent.Files([new FileRef(@"C:\Users\me\report.pdf"), new FileRef(@"C:\b.txt")], 3);

        Assert.Equal("report.pdf", s_text.Title(one));
        Assert.Equal("report.pdf and 2 more", s_text.Title(three));
    }

    [Fact]
    public void RowTitleIsTheGivenNameIfAny()
    {
        var content = Snippet("hello");
        Assert.Equal("hello", s_text.Title(new ItemPreview(1, content, false, null, s_now, null)));
        Assert.Equal("My note", s_text.Title(new ItemPreview(1, content, false, null, s_now, "My note")));
        Assert.Contains("My note", s_text.AccessibleName(new ItemPreview(1, content, false, null, s_now, "My note"), s_now), StringComparison.Ordinal);
    }

    [Fact]
    public void FolderTitleIsItsName()
    {
        Assert.Equal("Photos", s_text.Title(new PreviewContent.Files([new FileRef(@"D:\Photos\")], 1)));
    }

    [Theory]
    [InlineData(10, "now")]
    [InlineData(5 * 60, "5 min ago")]
    [InlineData(3 * 3600, "3 h ago")]
    [InlineData(2 * 86400, "2 d ago")]
    public void RecentTimesAreRelative(int secondsAgo, string expected)
    {
        Assert.Equal(expected, s_text.CopiedTime(s_now.AddSeconds(-secondsAgo), s_now));
    }

    [Fact]
    public void OldTimesAreDates()
    {
        var copied = s_now.AddDays(-30);

        Assert.Equal(copied.ToLocalTime().ToString("d", CultureInfo.GetCultureInfo("en-US")), s_text.CopiedTime(copied, s_now));
    }

    [Fact]
    public void DetailNamesTheSourceApp()
    {
        var row = Row(new SourceApp("notepad.exe", "Notepad"));
        var unnamed = Row(new SourceApp("tool.exe"));

        Assert.Equal("Notepad · now", s_text.Detail(row, s_now));
        Assert.Equal("tool.exe · now", s_text.Detail(unnamed, s_now));
        Assert.Equal("now", s_text.Detail(Row(null), s_now));
    }

    [Fact]
    public void AccessibleNameSaysEverything()
    {
        var row = Row(new SourceApp("chrome.exe", "Google Chrome"), pinned: true, hint: TextHint.Link);

        Assert.Equal("Link, https://example.com, Google Chrome · now, pinned", s_text.AccessibleName(row, s_now));
    }

    [Theory]
    [InlineData(false, false, "No items")]
    [InlineData(false, true, "No matches")]
    [InlineData(true, true, "History unavailable")]
    public void EmptyListSaysWhy(bool failed, bool searching, string expected)
    {
        Assert.Equal(expected, s_text.EmptyList(failed, searching));
    }

    [Theory]
    [InlineData(Category.All, "Clear all unpinned items")]
    [InlineData(Category.Text, "Clear unpinned text")]
    [InlineData(Category.Images, "Clear unpinned images")]
    [InlineData(Category.Files, "Clear unpinned files")]
    public void ClearButtonNamesTheTab(Category category, string expected)
    {
        Assert.Equal(expected, s_text.ClearTab(category));
    }

    [Fact]
    public void FarsiStringsAreUsed()
    {
        var farsi = Text("fa", "fa-IR");

        Assert.Equal("پیوند", farsi.Kind(Snippet("https://example.com", TextHint.Link)));
        Assert.Contains("640", farsi.Title(new PreviewContent.Image(ImageFormat.Png, 640, 480, null)), StringComparison.Ordinal);
    }

    private static PreviewContent.Text Snippet(string text, TextHint hint = TextHint.Plain) => new(text, false, hint);

    private static ItemPreview Row(SourceApp? app, bool pinned = false, TextHint hint = TextHint.Plain) =>
        new(1, Snippet("https://example.com", hint), pinned, app, s_now.AddSeconds(-5), null);

    private static PopupText Text(string language, string culture)
    {
        var strings = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Strings", language, "Resources.resw"))
            .Root!
            .Elements("data")
            .ToDictionary(data => (string)data.Attribute("name")!, data => (string?)data.Element("value") ?? "");
        return new PopupText(key => strings[key], CultureInfo.GetCultureInfo(culture));
    }
}

public sealed class PopupPlacementTests
{
    private static readonly ScreenRect s_work = new(0, 0, 1920, 1040);

    [Fact]
    public void HangsFromThePointerToItsRight()
    {
        Assert.Equal(new ScreenRect(100, 200, 500, 700), PopupPlacement.Frame(400, 500, new ScreenPoint(100, 200), s_work, rightToLeft: false));
    }

    [Fact]
    public void HangsToTheLeftInRightToLeftLayouts()
    {
        Assert.Equal(new ScreenRect(600, 200, 1000, 700), PopupPlacement.Frame(400, 500, new ScreenPoint(1000, 200), s_work, rightToLeft: true));
    }

    [Fact]
    public void MovesInsideTheWorkArea()
    {
        Assert.Equal(new ScreenRect(1520, 540, 1920, 1040), PopupPlacement.Frame(400, 500, new ScreenPoint(1800, 1000), s_work, rightToLeft: false));
        Assert.Equal(new ScreenRect(0, 540, 400, 1040), PopupPlacement.Frame(400, 500, new ScreenPoint(50, 1000), s_work, rightToLeft: true));
    }

    [Fact]
    public void KeepsTheTopAndLeadingEdgeOnATooSmallScreen()
    {
        var small = new ScreenRect(0, 0, 300, 300);

        Assert.Equal(new ScreenRect(0, 0, 400, 500), PopupPlacement.Frame(400, 500, new ScreenPoint(200, 200), small, rightToLeft: false));
        Assert.Equal(new ScreenRect(-100, 0, 300, 500), PopupPlacement.Frame(400, 500, new ScreenPoint(200, 200), small, rightToLeft: true));
    }

    [Fact]
    public void WorksOnASecondMonitorLeftOfTheFirst()
    {
        var left = new ScreenRect(-1920, 0, 0, 1040);

        Assert.Equal(new ScreenRect(-400, 100, 0, 600), PopupPlacement.Frame(400, 500, new ScreenPoint(-100, 100), left, rightToLeft: false));
    }
}

public sealed class PopupRenameTests
{
    private static PopupKeyCommand Key(PopupKeyAction action, string text = "") => new(action, Text: text);

    [Fact]
    public void TheFirstCharacterReplacesTheOldName()
    {
        var rename = new PopupRename(7, "hello");
        Assert.True(rename.IsFresh);

        Assert.Equal(PopupRenameStep.Editing, rename.Handle(Key(PopupKeyAction.Type, "N")));
        rename.Handle(Key(PopupKeyAction.Type, "ew"));
        Assert.Equal("New", rename.Text);
        Assert.False(rename.IsFresh);
        Assert.Equal(7, rename.Id);
    }

    [Fact]
    public void EndKeepsTheOldNameToTypeAfterIt()
    {
        var rename = new PopupRename(1, "hello");
        rename.Handle(Key(PopupKeyAction.End));
        rename.Handle(Key(PopupKeyAction.Type, " world"));
        rename.Handle(Key(PopupKeyAction.Backspace));
        Assert.Equal("hello worl", rename.Text);
        rename.Handle(Key(PopupKeyAction.DeleteWord));
        Assert.Equal("hello ", rename.Text);
    }

    [Fact]
    public void BackspaceOnTheOldNameClearsIt()
    {
        var rename = new PopupRename(1, "hello");
        rename.Handle(Key(PopupKeyAction.Backspace));
        Assert.Equal("", rename.Text);
    }

    [Fact]
    public void EnterSavesEscCancelsAndOtherKeysWait()
    {
        var rename = new PopupRename(1, "hello");
        Assert.Equal(PopupRenameStep.Editing, rename.Handle(Key(PopupKeyAction.Down)));
        Assert.Equal(PopupRenameStep.Editing, rename.Handle(Key(PopupKeyAction.NextTab)));
        Assert.True(rename.IsFresh);
        Assert.Equal("hello", rename.Text);
        Assert.Equal(PopupRenameStep.Save, rename.Handle(Key(PopupKeyAction.Pick)));
        Assert.Equal(PopupRenameStep.Cancel, rename.Handle(Key(PopupKeyAction.Close)));
    }
}
