using System.Security.Cryptography;
using Clipo.Core;
using Clipo.Services;

namespace Clipo.Tests;

public sealed class HistoryTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("clipo-tests-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void DatabasePathCreatesTheClipoFolder()
    {
        var path = History.DatabasePath(_folder);

        Assert.Equal(Path.Combine(_folder, "Clipo", "history.db"), path);
        Assert.True(Directory.Exists(Path.Combine(_folder, "Clipo")));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void FirstOpenCreatesAnEmptyHistoryAndSavesAKey()
    {
        var keys = new MemoryKeyStore();

        using var store = OpenReady(DatabasePath(), keys, expectedCount: 0);

        Assert.Equal(32, keys.Load()?.Length);
    }

    [Fact]
    public void ReopeningKeepsTheItems()
    {
        var path = DatabasePath();
        var keys = new MemoryKeyStore();
        using (var store = OpenReady(path, keys, expectedCount: 0))
        {
            var text = new ItemContent.Text(new TextContent("سلام"));
            Assert.IsType<Recorded.Added>(store.Record(new Capture(text, DateTime.UtcNow)));
        }

        using var reopened = OpenReady(path, keys, expectedCount: 1);
    }

    [Fact]
    public void MissingKeyIsAKeyProblem()
    {
        var path = DatabasePath();
        OpenReady(path, new MemoryKeyStore(), expectedCount: 0).Dispose();

        Assert.IsType<OpenResult.KeyProblem>(History.Open(path, new MemoryKeyStore()));
    }

    [Fact]
    public void WrongKeyIsAKeyProblem()
    {
        var path = DatabasePath();
        OpenReady(path, new MemoryKeyStore(), expectedCount: 0).Dispose();
        var other = new MemoryKeyStore();
        other.Save(RandomNumberGenerator.GetBytes(32));

        Assert.IsType<OpenResult.KeyProblem>(History.Open(path, other));
    }

    [Fact]
    public void FailingKeyStoreIsNotAKeyProblem()
    {
        var path = DatabasePath();
        OpenReady(path, new MemoryKeyStore(), expectedCount: 0).Dispose();

        // An unavailable credential store must not lead to offering a new history.
        Assert.IsType<OpenResult.Failed>(History.Open(path, new FailingKeyStore()));
    }

    [Fact]
    public void StartOverDeletesTheDatabaseAndTheKey()
    {
        var path = DatabasePath();
        var keys = new MemoryKeyStore();
        using (var store = OpenReady(path, keys, expectedCount: 0))
        {
            store.Record(new Capture(new ItemContent.Text(new TextContent("old")), DateTime.UtcNow));
        }

        Assert.True(History.StartOver(path, keys));

        Assert.False(File.Exists(path));
        Assert.Null(keys.Load());
        using var fresh = OpenReady(path, keys, expectedCount: 0);
    }

    [Fact]
    public void StartOverReportsAFailingKeyStore()
    {
        var path = DatabasePath();
        OpenReady(path, new MemoryKeyStore(), expectedCount: 0).Dispose();

        Assert.False(History.StartOver(path, new FailingKeyStore()));
    }

    private string DatabasePath() => History.DatabasePath(_folder);

    private static ClipStore OpenReady(string path, KeyStore keys, ulong expectedCount)
    {
        var ready = Assert.IsType<OpenResult.Ready>(History.Open(path, keys));
        Assert.Equal(expectedCount, ready.ItemCount);
        return ready.Store;
    }
}

/// <summary>In-memory stand-in for the Credential Manager.</summary>
internal sealed class MemoryKeyStore : IClearableKeyStore
{
    private readonly Lock _lock = new();
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

    public void Delete()
    {
        lock (_lock)
        {
            _key = null;
        }
    }
}

/// <summary>A key store whose every call fails, like a locked credential store.</summary>
internal sealed class FailingKeyStore : IClearableKeyStore
{
    public byte[]? Load() => throw new KeyStoreException.Failed("Win32 error 1312");

    public void Save(byte[] key) => throw new KeyStoreException.Failed("Win32 error 1312");

    public void Delete() => throw new KeyStoreException.Failed("Win32 error 1312");
}
