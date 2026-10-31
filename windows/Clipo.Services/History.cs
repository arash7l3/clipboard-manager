using System.Diagnostics;
using System.Runtime.Versioning;
using Clipo.Core;

namespace Clipo.Services;

/// <summary>What opening the history came to.</summary>
public abstract record OpenResult
{
    private OpenResult()
    {
    }

    /// <summary>Open, holding <paramref name="ItemCount"/> items. The caller owns <paramref name="Store"/>.</summary>
    public sealed record Ready(ClipStore Store, ulong ItemCount) : OpenResult;

    /// <summary>
    /// The database exists but its key is missing from the key store or does
    /// not open it. Only starting over helps, so ask the user first.
    /// </summary>
    public sealed record KeyProblem : OpenResult;

    /// <summary>Anything else, such as an unavailable credential store or a disk error. Worth retrying.</summary>
    public sealed record Failed : OpenResult;
}

/// <summary>
/// Opens the encrypted history kept by the core. Every call does disk and
/// credential-store I/O; the <c>Default</c> variants run off the UI thread.
/// </summary>
public static class History
{
    /// <summary>The Credential Manager target that holds the history key.</summary>
    public const string CredentialTarget = "io.github.arash7l3.Clipo/history-key";

    /// <summary>
    /// <c>Clipo\history.db</c> inside <paramref name="localAppData"/> (the
    /// user's local, non-roaming app data folder by default). Creates the
    /// <c>Clipo</c> folder, which inherits the user-only access of that folder.
    /// </summary>
    public static string DatabasePath(string? localAppData = null)
    {
        var root = localAppData ?? Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);
        var folder = Directory.CreateDirectory(Path.Combine(root, "Clipo"));
        return Path.Combine(folder.FullName, "history.db");
    }

    /// <summary>Opens the database at <paramref name="path"/> with its key in <paramref name="keyStore"/>, creating both on first run.</summary>
    public static OpenResult Open(string path, KeyStore keyStore)
    {
        try
        {
            var store = ClipStore.Open(path, keyStore);
            try
            {
                return new OpenResult.Ready(store, store.Count());
            }
            catch
            {
                store.Dispose();
                throw;
            }
        }
        catch (ClipoException e) when (e is ClipoException.KeyMissing or ClipoException.WrongKey or ClipoException.InvalidKey)
        {
            Trace.TraceError("history key is missing or does not open the database");
            return new OpenResult.KeyProblem();
        }
        catch (Exception e)
        {
            // Core errors carry codes and ids, never clipboard content.
            Trace.TraceError($"opening history failed: {Describe(e)}");
            return new OpenResult.Failed();
        }
    }

    /// <summary>
    /// Deletes the database at <paramref name="path"/> and then its key, so
    /// the next <see cref="Open"/> starts an empty history with a new key.
    /// Dispose every store on <paramref name="path"/> first. Returns whether
    /// both were deleted.
    /// </summary>
    public static bool StartOver(string path, IClearableKeyStore keyStore)
    {
        ArgumentNullException.ThrowIfNull(keyStore);
        try
        {
            // Database first: a key without a database is harmless, the
            // reverse would ask the user again.
            ClipoCore.DeleteDatabase(path);
            keyStore.Delete();
            Trace.TraceInformation("started a new history");
            return true;
        }
        catch (Exception e)
        {
            Trace.TraceError($"starting a new history failed: {Describe(e)}");
            return false;
        }
    }

    /// <summary><see cref="Open"/> on the user's history, off the calling thread.</summary>
    [SupportedOSPlatform("windows")]
    public static Task<OpenResult> OpenDefaultAsync() => Task.Run(() =>
    {
        try
        {
            return Open(DatabasePath(), new CredentialKeyStore(CredentialTarget));
        }
        catch (Exception e)
        {
            Trace.TraceError($"no history folder: {Describe(e)}");
            return new OpenResult.Failed();
        }
    });

    /// <summary><see cref="StartOver"/> on the user's history, off the calling thread.</summary>
    [SupportedOSPlatform("windows")]
    public static Task<bool> StartOverDefaultAsync() => Task.Run(() =>
    {
        try
        {
            return StartOver(DatabasePath(), new CredentialKeyStore(CredentialTarget));
        }
        catch (Exception e)
        {
            Trace.TraceError($"no history folder: {Describe(e)}");
            return false;
        }
    });

    private static string Describe(Exception e) => $"{e.GetType().Name}: {e.Message}";
}
