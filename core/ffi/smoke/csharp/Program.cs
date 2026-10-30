// Opens a store through the generated C# bindings, records, searches and
// checks the error mapping. Exits non-zero on the first failure.

using Clipo.Core;

var dir = Path.Combine(Path.GetTempPath(), $"clipo-smoke-{Guid.NewGuid()}");
var path = Path.Combine(dir, "history.db");
try
{
    Run(path);
}
finally
{
    if (Directory.Exists(dir))
    {
        Directory.Delete(dir, recursive: true);
    }
}
Console.WriteLine("C# smoke test passed");
return 0;

static void Run(string path)
{
    var keys = new MemoryKeyStore();
    using var store = ClipStore.Open(path, keys);
    Check(keys.Load()?.Length == 32, "a 32-byte key was saved");

    var start = new DateTime(2023, 11, 14, 22, 13, 20, DateTimeKind.Utc);
    var first = store.Record(new Capture(Text("سلام دنیا"), start)) is Recorded.Added added
        ? added.Item
        : throw new Exception("first capture is not added");
    Check(first.CreatedAt == start, "timestamps round-trip as DateTime");

    var image = new ImageContent(ImageFormat.Png, 1, 1, new byte[] { 1, 2, 3 });
    store.Record(new Capture(
        new ItemContent.Image(image),
        start.AddSeconds(1),
        new SourceApp("PhotosApp.exe", "Photos")));

    var again = store.Record(new Capture(Text("سلام دنیا"), start.AddSeconds(2)));
    Check(again is Recorded.Bumped bumped && bumped.Item.Id == first.Id, "repeated copy is bumped");

    var all = store.Search(new Query(Category.All));
    Check(all.Length == 2, "two items stored");
    Check(Plain(all[0]) == "سلام دنیا", "bumped item is first");

    var found = store.Search(new Query(Category.Text, "سلام"));
    Check(found.Length == 1 && found[0].Id == first.Id, "search finds the Persian text");
    Check(store.Search(new Query(Category.Images)).Length == 1, "images tab");

    var concealed = store.Record(new Capture(Text("password"), start, Concealed: true));
    Check(
        concealed is Recorded.Skipped { Reason: SkipReason.Concealed },
        "concealed capture is skipped");

    store.SetPinned(first.Id, true);
    Check(store.Get(first.Id)?.Pinned == true, "pin");

    try
    {
        store.Delete(9_999);
        Check(false, "deleting a missing item throws");
    }
    catch (ClipoException.ItemNotFound e)
    {
        Check(e.id == 9_999, "ItemNotFound carries the id");
    }

    Check(
        ClipoCore.TabCategories().SequenceEqual(new[]
        {
            Category.All, Category.Text, Category.Images, Category.Files, Category.Pinned,
        }),
        "tab order");
    Check(ClipoCore.DefaultHistoryLimits().MaxItems == 200, "default limit");

    try
    {
        ClipStore.Open(path, new MemoryKeyStore()).Dispose();
        Check(false, "opening with an empty key store throws");
    }
    catch (ClipoException.KeyMissing)
    {
    }

    try
    {
        ClipStore.Open(path, new FailingKeyStore()).Dispose();
        Check(false, "a failing key store throws");
    }
    catch (ClipoException.KeyStore e)
    {
        Check(e.message == "OSStatus -25308", "key store message is passed through");
    }

    using var reopened = ClipStore.Open(path, keys);
    Check(reopened.Count() == 2, "history survives a reopen");
}

static ItemContent Text(string plain) => new ItemContent.Text(new TextContent(plain));

static string? Plain(ClipItem item) =>
    item.Content is ItemContent.Text text ? text.TextValue.Plain : null;

static void Check(bool condition, string what)
{
    if (!condition)
    {
        Console.Error.WriteLine($"FAILED: {what}");
        Environment.Exit(1);
    }
}

/// <summary>In-memory stand-in for the Credential Manager.</summary>
sealed class MemoryKeyStore : KeyStore
{
    private readonly object _lock = new();
    private byte[]? _key;

    public byte[]? Load()
    {
        lock (_lock)
        {
            return _key;
        }
    }

    public void Save(byte[] key)
    {
        lock (_lock)
        {
            _key = key;
        }
    }
}

/// <summary>A key store whose reads fail.</summary>
sealed class FailingKeyStore : KeyStore
{
    public byte[]? Load() => throw new KeyStoreException.Failed("OSStatus -25308");

    public void Save(byte[] key)
    {
    }
}
