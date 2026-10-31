using Clipo.Core;
using Clipo.Services;

namespace Clipo.Tests;

public sealed class ExtrasTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("clipo-extras-tests-").FullName;
    private readonly List<ClipStore> _stores = [];

    public void Dispose()
    {
        foreach (var store in _stores)
        {
            store.Dispose();
        }
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task SnippetsAreAddedChangedListedAndDeleted()
    {
        var store = Open("a");
        var extras = new Extras(() => store);

        Assert.True(await extras.SaveSnippetAsync(null, "Greeting", "Hello"));
        var saved = Assert.Single((await extras.SnippetsAsync())!);
        Assert.Equal(("Greeting", "Hello"), (saved.Title, saved.Body));

        Assert.True(await extras.SaveSnippetAsync(saved.Id, "Greeting", "سلام"));
        Assert.Equal("سلام", Assert.Single((await extras.SnippetsAsync())!).Body);

        Assert.True(await extras.DeleteSnippetAsync(saved.Id));
        Assert.Empty((await extras.SnippetsAsync())!);
    }

    [Fact]
    public async Task BlankSnippetIsRefused()
    {
        var store = Open("a");
        var extras = new Extras(() => store);

        Assert.False(await extras.SaveSnippetAsync(null, "  ", "text"));
        Assert.False(await extras.SaveSnippetAsync(null, "title", ""));
        Assert.Empty((await extras.SnippetsAsync())!);
    }

    [Fact]
    public async Task ClosedHistoryFailsWithoutThrowing()
    {
        var extras = new Extras(() => null);

        Assert.Null(await extras.SnippetsAsync());
        Assert.False(await extras.SaveSnippetAsync(null, "a", "b"));
        Assert.False(await extras.DeleteSnippetAsync(1));
        Assert.Null(await extras.ExportAsync());
        Assert.Equal(ImportOutcome.Failed, (await extras.ImportAsync([1, 2, 3])).Outcome);
    }

    [Fact]
    public async Task ExportThenImportMovesHistoryAndSnippets()
    {
        var source = Open("source");
        source.Record(new Capture(new ItemContent.Text(new TextContent("copied text")), DateTime.UtcNow));
        var from = new Extras(() => source);
        await from.SaveSnippetAsync(null, "Sig", "Regards");
        var data = await from.ExportAsync();
        Assert.NotNull(data);

        var target = Open("target");
        var into = new Extras(() => target);
        var result = await into.ImportAsync(data);

        Assert.Equal(ImportOutcome.Imported, result.Outcome);
        Assert.Equal(1UL, result.Summary!.ItemsAdded);
        Assert.Equal(1UL, result.Summary.SnippetsAdded);
        Assert.Equal(1UL, target.Count());

        // The same file again adds nothing.
        var again = await into.ImportAsync(data);
        Assert.Equal(0UL, again.Summary!.ItemsAdded);
        Assert.Equal(1UL, again.Summary.ItemsDuplicate);
        Assert.Equal(1UL, again.Summary.SnippetsDuplicate);
    }

    [Fact]
    public async Task ImportRefusesGarbage()
    {
        var store = Open("a");
        var extras = new Extras(() => store);

        Assert.Equal(ImportOutcome.Invalid, (await extras.ImportAsync([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12])).Outcome);
        Assert.Equal(ImportOutcome.Invalid, (await extras.ImportAsync([])).Outcome);
    }

    [Theory]
    [InlineData("Plain", "Plain")]
    [InlineData("Fish & Chips", "Fish && Chips")]
    [InlineData("two\r\nlines", "two lines")]
    public void MenuTitleIsOneLineWithoutMnemonics(string title, string expected) =>
        Assert.Equal(expected, Extras.MenuTitle(title));

    [Fact]
    public void LongMenuTitleIsCutWithoutSplittingASurrogatePair()
    {
        var title = new string('a', Extras.MaxMenuTitleLength - 1) + "😀😀";

        var shown = Extras.MenuTitle(title);

        Assert.Equal(new string('a', Extras.MaxMenuTitleLength - 1) + "…", shown);
    }

    private ClipStore Open(string name)
    {
        var path = History.DatabasePath(Path.Combine(_folder, name));
        var result = History.Open(path, new MemoryKeyStore());
        var store = Assert.IsType<OpenResult.Ready>(result).Store;
        _stores.Add(store);
        return store;
    }
}
