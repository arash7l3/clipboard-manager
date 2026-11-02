using Clipo.Core;
using Clipo.Services;

namespace Clipo.Tests;

public sealed class EmojiModelTests
{
    private static Emoji E(string text, EmojiGroup group) => new(text, "name " + text, group);

    private static EmojiSection Section(EmojiGroup group, params string[] texts) =>
        new(group, [.. texts.Select(text => E(text, group))]);

    private static EmojiModel Model(
        IReadOnlyList<Emoji>? recent = null,
        Func<string, IReadOnlyList<Emoji>>? search = null,
        IReadOnlySet<EmojiGroup>? hidden = null) =>
        new(
            [
                Section(EmojiGroup.SmileysAndPeople, "a", "b", "c", "d", "e"),
                Section(EmojiGroup.AnimalsAndNature, "f", "g"),
                Section(EmojiGroup.Flags, "h", "i"),
            ],
            search ?? (_ => []),
            () => recent ?? [],
            hidden ?? EmojiModel.WindowsHidden)
        {
            Columns = 3,
            PageRows = 2,
        };

    private static string Selected(EmojiModel model) => model.SelectedEmoji?.Text ?? "";

    [Fact]
    public void ListsTheGroupsWithoutTheHiddenOnes()
    {
        var model = Model();

        Assert.Equal([EmojiGroup.SmileysAndPeople, EmojiGroup.AnimalsAndNature], model.Sections.Select(section => section.Group));
        Assert.Equal("a", Selected(model));
        Assert.Null(model.IndexOf(EmojiGroup.Flags));
        Assert.Equal(1, model.IndexOf(EmojiGroup.AnimalsAndNature));
    }

    [Fact]
    public async Task RecentEmojiComeFirstAndHiddenOnesAreDropped()
    {
        var model = Model(recent: [E("g", EmojiGroup.AnimalsAndNature), E("h", EmojiGroup.Flags)]);
        var changed = 0;
        model.SectionsChanged += (_, _) => changed++;

        await model.ReloadRecentAsync();

        Assert.Equal(1, changed);
        Assert.Equal(EmojiSectionKind.Recent, model.Sections[0].Kind);
        Assert.Equal(["g"], model.Sections[0].Emoji.Select(emoji => emoji.Text));
        Assert.Equal(0, model.IndexOfRecent());
        Assert.Equal(3, model.Sections.Count);
    }

    [Fact]
    public void SearchShowsOneBlockOfResultsWithoutHiddenEmoji()
    {
        var model = Model(search: text => [E("b", EmojiGroup.SmileysAndPeople), E("h", EmojiGroup.Flags)]);

        model.SetSearchText("na");

        var results = Assert.Single(model.Sections);
        Assert.Equal(EmojiSectionKind.Results, results.Kind);
        Assert.Equal(["b"], results.Emoji.Select(emoji => emoji.Text));
        Assert.Equal("b", Selected(model));

        model.SetSearchText("  ");
        Assert.Equal(2, model.Sections.Count);
    }

    [Fact]
    public void SearchWithNoResultsLeavesNoSelection()
    {
        var model = Model();

        model.SetSearchText("zzz");

        Assert.Empty(model.Sections);
        Assert.Null(model.Selection);
        Assert.False(model.Move(EmojiMove.Right));
    }

    [Fact]
    public void LeftAndRightStepAcrossSectionsAndStopAtTheEnds()
    {
        var model = Model();

        Assert.False(model.Move(EmojiMove.Left));
        for (var i = 0; i < 4; i++)
        {
            Assert.True(model.Move(EmojiMove.Right));
        }
        Assert.Equal("e", Selected(model));
        Assert.True(model.Move(EmojiMove.Right));
        Assert.Equal("f", Selected(model));
        Assert.True(model.Move(EmojiMove.Right));
        Assert.False(model.Move(EmojiMove.Right));
        Assert.Equal("g", Selected(model));
        Assert.True(model.Move(EmojiMove.Left));
        Assert.Equal("f", Selected(model));
    }

    [Fact]
    public void UpAndDownKeepTheColumnAcrossSections()
    {
        var model = Model();

        model.Select(new EmojiPosition(0, 1));
        Assert.True(model.Move(EmojiMove.RowDown));
        Assert.Equal("e", Selected(model));
        // Past the last row: the next section, same column.
        Assert.True(model.Move(EmojiMove.RowDown));
        Assert.Equal("g", Selected(model));
        Assert.False(model.Move(EmojiMove.RowDown));
        Assert.True(model.Move(EmojiMove.RowUp));
        Assert.Equal("e", Selected(model));
        Assert.True(model.Move(EmojiMove.RowUp));
        Assert.Equal("b", Selected(model));
        Assert.False(model.Move(EmojiMove.RowUp));
    }

    [Fact]
    public void ADownMoveIntoAShortLastRowLandsOnItsLastEmoji()
    {
        var model = Model();

        model.Select(new EmojiPosition(0, 2));
        Assert.True(model.Move(EmojiMove.RowDown));
        Assert.Equal("e", Selected(model));
    }

    [Fact]
    public void PagesAndEndsMoveFar()
    {
        var model = Model();

        Assert.True(model.Move(EmojiMove.Last));
        Assert.Equal("g", Selected(model));
        Assert.True(model.Move(EmojiMove.PageUp));
        // Two rows up from g: e, then b.
        Assert.Equal("b", Selected(model));
        Assert.True(model.Move(EmojiMove.PageDown));
        Assert.True(model.Move(EmojiMove.First));
        Assert.Equal("a", Selected(model));
    }

    [Fact]
    public void ColumnsAndPageRowsStayPositive()
    {
        var model = Model();

        model.Columns = 0;
        model.PageRows = -3;

        Assert.Equal(1, model.Columns);
        Assert.Equal(1, model.PageRows);
    }

    [Fact]
    public async Task AFailingRecentLoaderShowsNoRecentSection()
    {
        var model = new EmojiModel(
            [Section(EmojiGroup.Objects, "a")], _ => [], () => throw new InvalidOperationException("boom"), EmojiModel.WindowsHidden);

        await model.ReloadRecentAsync();

        Assert.Equal(EmojiSectionKind.Group, Assert.Single(model.Sections).Kind);
    }

    [Fact]
    public void TheCoreListHasEverySectionButFlags()
    {
        var model = new EmojiModel(() => []);

        Assert.DoesNotContain(EmojiGroup.Flags, model.Groups);
        Assert.Equal(7, model.Groups.Count());
        Assert.NotEmpty(model.Sections[0].Emoji);
        model.SetSearchText("smil");
        Assert.Contains(model.Sections[0].Emoji, emoji => emoji.Name.Contains("smiling", StringComparison.Ordinal));
        model.SetSearchText("mongolia");
        Assert.Empty(model.Sections);
    }
}
