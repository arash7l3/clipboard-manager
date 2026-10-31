using Clipo.Core;
using Clipo.Services;

namespace Clipo.Tests;

public sealed class CaptureTests : IDisposable
{
    private static readonly DateTime s_date = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
    private static readonly SourceApp s_app = new("Notepad.exe", "Notepad");

    private readonly string _folder = Directory.CreateTempSubdirectory("clipo-capture-tests-").FullName;
    private readonly FakeImages _images = new();

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task TextIsKeptWithItsSource()
    {
        var capture = await BuildAsync(new ClipboardContent.Text("سلام دنیا"));

        var text = Assert.IsType<ItemContent.Text>(capture?.Content).TextValue;
        Assert.Equal("سلام دنیا", text.Plain);
        Assert.Null(text.Rich);
        Assert.Equal(s_app, capture.SourceApp);
        Assert.Equal(s_date, capture.CapturedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \r\n\t ")]
    public async Task BlankTextIsNotKept(string text)
    {
        Assert.Null(await BuildAsync(new ClipboardContent.Text(text)));
    }

    [Fact]
    public async Task RtfIsPreferredOverHtml()
    {
        var capture = await BuildAsync(new ClipboardContent.Text("x", Rtf: "{\\rtf1 x}", Html: "<b>x</b>"));

        var rich = Assert.IsType<ItemContent.Text>(capture?.Content).TextValue.Rich;
        Assert.Equal(new RichText(RichTextFormat.Rtf, "{\\rtf1 x}"), rich);
    }

    [Fact]
    public async Task HtmlIsKeptWhenThereIsNoRtf()
    {
        var capture = await BuildAsync(new ClipboardContent.Text("x", Html: "<b>x</b>"));

        var rich = Assert.IsType<ItemContent.Text>(capture?.Content).TextValue.Rich;
        Assert.Equal(new RichText(RichTextFormat.Html, "<b>x</b>"), rich);
    }

    [Fact]
    public async Task RichFormsOverTheLimitAreDropped()
    {
        var capture = await BuildAsync(new ClipboardContent.Text("x", Rtf: new string('r', 20), Html: new string('h', 20)), maxItemBytes: 10);

        Assert.Null(Assert.IsType<ItemContent.Text>(capture?.Content).TextValue.Rich);
    }

    [Fact]
    public async Task FilesKeepTheirSizes()
    {
        var file = Path.Combine(_folder, "a.txt");
        await File.WriteAllTextAsync(file, "12345");
        var folder = Directory.CreateDirectory(Path.Combine(_folder, "dir")).FullName;
        var gone = Path.Combine(_folder, "gone.txt");

        var capture = await BuildAsync(new ClipboardContent.Files([file, folder, gone]));

        var files = Assert.IsType<ItemContent.Files>(capture?.Content).FilesValue;
        Assert.Equal([new FileRef(file, 5), new FileRef(folder), new FileRef(gone)], files);
    }

    [Fact]
    public async Task NoFilesAreNotKept()
    {
        Assert.Null(await BuildAsync(new ClipboardContent.Files([])));
        Assert.Null(await BuildAsync(new ClipboardContent.Files([""])));
    }

    [Fact]
    public async Task TooManyFilesAreNotKept()
    {
        var paths = Enumerable.Range(0, CaptureBuilder.MaxFiles + 1).Select(i => Path.Combine(_folder, $"{i}.txt")).ToArray();

        Assert.Null(await BuildAsync(new ClipboardContent.Files(paths)));
    }

    [Fact]
    public async Task PngIsStoredAsItIs()
    {
        var png = new byte[] { 1, 2, 3 };
        _images.Result = new ProcessedImage(40, 30, ImageFormat.Png, png, [9]);

        var capture = await BuildAsync(new ClipboardContent.Image(png, IsPng: true));

        var image = Assert.IsType<ItemContent.Image>(capture?.Content).ImageValue;
        Assert.Equal((ImageFormat.Png, 40u, 30u), (image.Format, image.Width, image.Height));
        Assert.Equal(png, image.Data);
        Assert.Equal([9], image.Thumbnail);
        Assert.Equal((png, true, CaptureBuilder.MaxPixels, CaptureBuilder.ThumbnailPixels), _images.Call);
    }

    [Fact]
    public async Task BitmapIsHandedOverAsABmpFile()
    {
        var dib = new byte[40 + 16];
        dib[0] = 40;
        dib[4] = 2;
        dib[8] = 2;
        dib[12] = 1;
        dib[14] = 24;
        _images.Result = new ProcessedImage(2, 2, ImageFormat.Png, [7, 7], null);

        var capture = await BuildAsync(new ClipboardContent.Image(dib, IsPng: false));

        Assert.Equal([7, 7], Assert.IsType<ItemContent.Image>(capture?.Content).ImageValue.Data);
        Assert.NotNull(_images.Call);
        Assert.False(_images.Call.Value.IsPng);
        Assert.Equal("BM", System.Text.Encoding.ASCII.GetString(_images.Call.Value.Data[..2]));
        Assert.Equal(dib.Length + 14, _images.Call.Value.Data.Length);
    }

    [Fact]
    public async Task BitmapThatIsNoDibIsNotKept()
    {
        Assert.Null(await BuildAsync(new ClipboardContent.Image(new byte[100], IsPng: false)));
        Assert.Null(_images.Call);
    }

    [Fact]
    public async Task PngOverTheLimitIsNotDecoded()
    {
        Assert.Null(await BuildAsync(new ClipboardContent.Image(new byte[20], IsPng: true), maxItemBytes: 10));
        Assert.Null(_images.Call);
    }

    [Fact]
    public async Task ImageThatCannotBeProcessedIsNotKept()
    {
        _images.Result = null;

        Assert.Null(await BuildAsync(new ClipboardContent.Image([1], IsPng: true)));
    }

    [Fact]
    public async Task ReencodedImageOverTheLimitIsNotKept()
    {
        _images.Result = new ProcessedImage(1, 1, ImageFormat.Png, new byte[20], null);

        Assert.Null(await BuildAsync(new ClipboardContent.Image([1], IsPng: true), maxItemBytes: 10));
    }

    [Fact]
    public async Task RecorderStoresAndThenBumpsTheSameText()
    {
        using var store = OpenStore();
        var recorder = new ClipboardRecorder(new CaptureBuilder(_images));
        var change = new ClipboardChange(new ClipboardContent.Text("hello"), s_app, s_date);

        Assert.IsType<Recorded.Added>(await recorder.RecordAsync(change, store));
        Assert.IsType<Recorded.Bumped>(await recorder.RecordAsync(change, store));
        Assert.Equal(1ul, store.Count());
    }

    [Fact]
    public async Task RecorderReturnsNullForWhatIsNotWorthKeeping()
    {
        using var store = OpenStore();
        var recorder = new ClipboardRecorder(new CaptureBuilder(_images));

        Assert.Null(await recorder.RecordAsync(new ClipboardChange(new ClipboardContent.Text(" "), s_app, s_date), store));
        Assert.Equal(0ul, store.Count());
    }

    [Fact]
    public async Task RecorderReturnsNullWhenTheStoreFails()
    {
        var store = OpenStore();
        store.Dispose();
        var recorder = new ClipboardRecorder(new CaptureBuilder(_images));

        Assert.Null(await recorder.RecordAsync(new ClipboardChange(new ClipboardContent.Text("x"), s_app, s_date), store));
    }

    [Fact]
    public async Task RulesOfTheCoreStillApplyToWhatIsRecorded()
    {
        using var store = OpenStore();
        store.SetRules(CaptureSettings.Rules(privateMode: true));
        var recorder = new ClipboardRecorder(new CaptureBuilder(_images));

        var result = await recorder.RecordAsync(new ClipboardChange(new ClipboardContent.Text("secret"), s_app, s_date), store);

        Assert.IsType<Recorded.Skipped>(result);
        Assert.Equal(0ul, store.Count());
    }

    [Fact]
    public void DefaultRulesIgnorePasswordManagers()
    {
        var rules = CaptureSettings.Rules();

        Assert.False(rules.PrivateMode);
        Assert.Contains("KeePassXC.exe", rules.IgnoredApps);
        Assert.Null(rules.AutoClearAfter);
    }

    [Theory]
    [InlineData("KeePassXC.exe", false, false)]
    [InlineData("keepassxc.EXE", false, false)]
    [InlineData("Notepad.exe", false, true)]
    [InlineData("Notepad.exe", true, false)]
    public void ReadingIsAllowedByAppAndPrivateMode(string app, bool privateMode, bool allowed)
    {
        var rules = CaptureSettings.Rules(privateMode);

        Assert.Equal(allowed, CaptureSettings.AllowsReading(new SourceApp(app), rules));
    }

    [Fact]
    public void ReadingIsAllowedWhenTheSourceIsUnknown()
    {
        Assert.True(CaptureSettings.AllowsReading(null, CaptureSettings.Rules()));
        Assert.False(CaptureSettings.AllowsReading(null, CaptureSettings.Rules(privateMode: true)));
    }

    private Task<Capture?> BuildAsync(ClipboardContent content, long maxItemBytes = 1024 * 1024) =>
        new CaptureBuilder(_images).BuildAsync(new ClipboardChange(content, s_app, s_date), maxItemBytes);

    private ClipStore OpenStore()
    {
        var path = History.DatabasePath(_folder);
        return Assert.IsType<OpenResult.Ready>(History.Open(path, new MemoryKeyStore())).Store;
    }

    private sealed class FakeImages : IImageProcessor
    {
        public ProcessedImage? Result { get; set; }

        public (byte[] Data, bool IsPng, long MaxPixels, int ThumbnailPixels)? Call { get; private set; }

        public Task<ProcessedImage?> ProcessAsync(byte[] encoded, bool isPng, long maxPixels, int thumbnailPixels)
        {
            Call = (encoded, isPng, maxPixels, thumbnailPixels);
            return Task.FromResult(Result);
        }

        public Task<byte[]?> ToBitmapAsync(byte[] encoded) => Task.FromResult<byte[]?>(null);
    }
}
