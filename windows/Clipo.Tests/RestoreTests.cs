using System.Buffers.Binary;
using System.Text;
using Clipo.Core;
using Clipo.Services;

namespace Clipo.Tests;

public sealed class RestoreTests : IDisposable
{
    private static readonly ClipItem s_item = new(
        7, new ItemContent.Text(new TextContent("hello")), false, null, DateTime.UtcNow, DateTime.UtcNow);

    private readonly string _folder = Directory.CreateTempSubdirectory("clipo-restore-tests-").FullName;
    private readonly List<string> _calls = [];

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task RestoresWithoutPastingByDefault()
    {
        var outcome = await Restorer(autoPaste: false).RestoreAsync(7);

        Assert.Equal(RestoreOutcome.Restored, outcome);
        // The bump runs in the background, so it may come at any point after the write.
        Assert.Equal(["load 7", "write"], Calls().Where(call => !call.StartsWith("bump", StringComparison.Ordinal)));
        await WaitForAsync("bump 7");
    }

    [Fact]
    public async Task PastesAfterRestoringWhenAutoPasteIsOn()
    {
        var outcome = await Restorer(autoPaste: true).RestoreAsync(7);

        Assert.Equal(RestoreOutcome.Pasted, outcome);
        Assert.Equal(["load 7", "write", "paste"], Calls().Where(call => !call.StartsWith("bump", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PlainRestoreWritesThePlainTextOnly()
    {
        ItemContent? written = null;
        var restorer = new ItemRestorer(
            _ => throw new InvalidOperationException("the full item is not needed"),
            content =>
            {
                written = content;
                return Task.FromResult(true);
            },
            id => Record($"bump {id}"),
            () => false,
            () => Record("paste"),
            id => id == 7 ? "just text" : null);

        Assert.Equal(RestoreOutcome.Restored, await restorer.RestoreAsync(7, plain: true));
        Assert.Equal("just text", Assert.IsType<ItemContent.Text>(written).TextValue.Plain);
        Assert.Null(Assert.IsType<ItemContent.Text>(written).TextValue.Rich);
        await WaitForAsync("bump 7");
    }

    [Fact]
    public async Task PlainRestoreOfAnItemWithoutTextIsUnavailable()
    {
        var restorer = new ItemRestorer(
            _ => s_item, _ => throw new InvalidOperationException("nothing to write"), _ => { }, () => true, () => { }, _ => null);

        Assert.Equal(RestoreOutcome.Unavailable, await restorer.RestoreAsync(7, plain: true));
    }

    [Fact]
    public async Task PlainRestoreWithoutALoaderIsUnavailable()
    {
        Assert.Equal(RestoreOutcome.Unavailable, await Restorer(autoPaste: false).RestoreAsync(7, plain: true));
    }

    [Fact]
    public async Task SnippetIsWrittenAndPastedWithoutMovingAnything()
    {
        var outcome = await Restorer(autoPaste: true).RestoreTextAsync("signature");

        Assert.Equal(RestoreOutcome.Pasted, outcome);
        Assert.Equal(["write", "paste"], Calls());
    }

    [Fact]
    public async Task EmptySnippetIsUnavailable()
    {
        Assert.Equal(RestoreOutcome.Unavailable, await Restorer(autoPaste: true).RestoreTextAsync(""));
        Assert.Empty(Calls());
    }

    [Fact]
    public async Task MissingItemIsUnavailableAndNothingIsWritten()
    {
        var outcome = await Restorer(autoPaste: true, found: false).RestoreAsync(7);

        Assert.Equal(RestoreOutcome.Unavailable, outcome);
        Assert.Equal(["load 7"], Calls());
    }

    [Fact]
    public async Task FailingLoadIsUnavailable()
    {
        var restorer = new ItemRestorer(_ => throw new InvalidOperationException(), _ => Task.FromResult(true), _ => { }, () => true, () => { });

        Assert.Equal(RestoreOutcome.Unavailable, await restorer.RestoreAsync(7));
    }

    [Fact]
    public async Task RefusedWriteIsUnavailableAndNothingIsPasted()
    {
        var outcome = await Restorer(autoPaste: true, writes: false).RestoreAsync(7);

        Assert.Equal(RestoreOutcome.Unavailable, outcome);
        Assert.Equal(["load 7", "write"], Calls());
    }

    [Fact]
    public void CfHtmlRoundTrips()
    {
        const string Fragment = "<p dir=\"rtl\">سلام <b>world</b></p>";

        var data = CfHtml.Build(Fragment);

        Assert.Equal(0, data[^1]);
        Assert.Equal(Fragment, CfHtml.ExtractFragment(data));
        Assert.StartsWith("Version:0.9\r\nStartHTML:", Encoding.UTF8.GetString(data), StringComparison.Ordinal);
    }

    [Fact]
    public void CfHtmlOffsetsPointAtTheWholeDocument()
    {
        var data = CfHtml.Build("x");
        var text = Encoding.UTF8.GetString(data);
        int Offset(string name) => int.Parse(text.AsSpan(text.IndexOf(name, StringComparison.Ordinal) + name.Length, 10), System.Globalization.CultureInfo.InvariantCulture);

        Assert.StartsWith("<html>", Encoding.UTF8.GetString(data[Offset("StartHTML:")..]), StringComparison.Ordinal);
        Assert.EndsWith("</html>", Encoding.UTF8.GetString(data[..Offset("EndHTML:")]), StringComparison.Ordinal);
    }

    [Fact]
    public void DibFromPixelsIsBottomUp()
    {
        // Two rows of one pixel: top blue, bottom red.
        byte[] pixels = [255, 0, 0, 255, 0, 0, 255, 255];

        var dib = Dib.FromBgra(1, 2, pixels);

        Assert.True(Dib.TryReadSize(dib, out var width, out var height));
        Assert.Equal((1u, 2u), (width, height));
        Assert.Equal(32, BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14)));
        Assert.Equal([0, 0, 255, 255, 255, 0, 0, 255], dib[40..]);
        Assert.NotNull(Dib.ToBmpFile(dib));
    }

    [Fact]
    public void DibFromPixelsChecksTheSize()
    {
        Assert.Throws<ArgumentException>(() => Dib.FromBgra(2, 2, new byte[4]));
        Assert.Throws<ArgumentException>(() => Dib.FromBgra(0, 2, []));
    }

    [Fact]
    public void DropFilesListsWidePaths()
    {
        var block = DropFiles.Build([@"C:\a.txt", @"D:\سند.docx"]);

        Assert.Equal(20u, BinaryPrimitives.ReadUInt32LittleEndian(block));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(16)));
        Assert.Equal("C:\\a.txt\0D:\\سند.docx\0\0", Encoding.Unicode.GetString(block.AsSpan(20)));
    }

    [Fact]
    public void SettingsDefaultToNoAutoPaste()
    {
        Assert.False(new AppSettingsStore(Path.Combine(_folder, "settings.json")).Load().AutoPaste);
    }

    [Fact]
    public void SettingsAreSavedAndLoaded()
    {
        var store = new AppSettingsStore(Path.Combine(_folder, "settings.json"));

        Assert.True(store.Save(new AppSettings { AutoPaste = true }));

        Assert.True(store.Load().AutoPaste);
        Assert.Contains("\"autoPaste\": true", File.ReadAllText(Path.Combine(_folder, "settings.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void UnreadableSettingsGiveTheDefaults()
    {
        var path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, "{ not json");

        Assert.Equal(new AppSettings(), new AppSettingsStore(path).Load());
    }

    [Fact]
    public void SettingsLiveBesideTheHistory()
    {
        Assert.Equal(Path.Combine(_folder, "Clipo", "settings.json"), AppSettingsStore.DefaultPath(_folder));
    }

    private ItemRestorer Restorer(bool autoPaste, bool found = true, bool writes = true) => new(
        id =>
        {
            Record($"load {id}");
            return found ? s_item : null;
        },
        _ =>
        {
            Record("write");
            return Task.FromResult(writes);
        },
        id => Record($"bump {id}"),
        () => autoPaste,
        () => Record("paste"))
    {
        Delay = TimeSpan.Zero,
    };

    private void Record(string call)
    {
        lock (_calls)
        {
            _calls.Add(call);
        }
    }

    private string[] Calls()
    {
        lock (_calls)
        {
            return [.. _calls];
        }
    }

    private async Task WaitForAsync(string call)
    {
        for (var i = 0; i < 100 && !Calls().Contains(call); i++)
        {
            await Task.Delay(10);
        }
        Assert.Contains(call, Calls());
    }
}
