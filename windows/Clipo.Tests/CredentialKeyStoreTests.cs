using System.Runtime.Versioning;
using System.Security.Cryptography;
using Clipo.Services;

namespace Clipo.Tests;

/// <summary>A test that needs Windows; skipped elsewhere.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Needs the Windows Credential Manager";
        }
    }
}

/// <summary>Uses the real Credential Manager under a target only these tests use.</summary>
[SupportedOSPlatform("windows")]
public sealed class CredentialKeyStoreTests : IDisposable
{
    private readonly CredentialKeyStore _store = new($"io.github.arash7l3.Clipo.Tests/{Guid.NewGuid()}");

    public void Dispose() => _store.Delete();

    [WindowsFact]
    public void LoadWithoutAKeyReturnsNull() => Assert.Null(_store.Load());

    [WindowsFact]
    public void SaveThenLoadReturnsTheKey()
    {
        var key = RandomNumberGenerator.GetBytes(32);

        _store.Save(key);

        Assert.Equal(key, _store.Load());
    }

    [WindowsFact]
    public void SaveReplacesTheKey()
    {
        _store.Save(RandomNumberGenerator.GetBytes(32));
        var second = RandomNumberGenerator.GetBytes(32);

        _store.Save(second);

        Assert.Equal(second, _store.Load());
    }

    [WindowsFact]
    public void DeleteRemovesTheKeyAndSucceedsWhenThereIsNone()
    {
        _store.Save(RandomNumberGenerator.GetBytes(32));

        _store.Delete();
        _store.Delete();

        Assert.Null(_store.Load());
    }

    [WindowsFact]
    public void HistoryOpensWithTheCredentialManager()
    {
        var folder = Directory.CreateTempSubdirectory("clipo-tests-").FullName;
        try
        {
            var path = History.DatabasePath(folder);
            var first = Assert.IsType<OpenResult.Ready>(History.Open(path, _store));
            first.Store.Dispose();

            var again = Assert.IsType<OpenResult.Ready>(History.Open(path, _store));
            again.Store.Dispose();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
