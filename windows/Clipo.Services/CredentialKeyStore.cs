using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Clipo.Core;

namespace Clipo.Services;

/// <summary>A key store the app can also clear, to start a new history.</summary>
public interface IClearableKeyStore : KeyStore
{
    /// <summary>Deletes the stored key. Succeeds if there is none.</summary>
    void Delete();
}

/// <summary>
/// Keeps the history key as a generic credential in the Windows Credential
/// Manager, for this user on this machine only (it does not roam). Windows
/// encrypts it with the user's logon credentials.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class CredentialKeyStore : IClearableKeyStore
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    /// <param name="target">The credential's name, unique to this key.</param>
    public CredentialKeyStore(string target)
    {
        ArgumentException.ThrowIfNullOrEmpty(target);
        Target = target;
    }

    /// <summary>The credential's name in the Credential Manager.</summary>
    public string Target { get; }

    public unsafe byte[]? Load()
    {
        if (!CredRead(Target, CredTypeGeneric, 0, out var credential))
        {
            var error = Marshal.GetLastPInvokeError();
            // A locked or unavailable credential store is not the same as
            // "no key", which would lead to offering a new history.
            return error == ErrorNotFound ? null : throw Failure(error);
        }
        try
        {
            return new ReadOnlySpan<byte>(credential->CredentialBlob, (int)credential->CredentialBlobSize).ToArray();
        }
        finally
        {
            CredFree(credential);
        }
    }

    public unsafe void Save(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        fixed (char* target = Target)
        fixed (byte* blob = key)
        {
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = target,
                CredentialBlobSize = (uint)key.Length,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
            };
            // Replaces any credential with the same target.
            if (!CredWrite(&credential, 0))
            {
                throw Failure(Marshal.GetLastPInvokeError());
            }
        }
    }

    public void Delete()
    {
        if (!CredDelete(Target, CredTypeGeneric, 0))
        {
            var error = Marshal.GetLastPInvokeError();
            if (error != ErrorNotFound)
            {
                throw Failure(error);
            }
        }
    }

    private static KeyStoreException.Failed Failure(int error) => new($"Win32 error {error}");

    /// <summary>CREDENTIALW from wincred.h.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct Credential
    {
        public uint Flags;
        public uint Type;
        public char* TargetName;
        public char* Comment;
        public uint LastWrittenLow;
        public uint LastWrittenHigh;
        public uint CredentialBlobSize;
        public byte* CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public char* TargetAlias;
        public char* UserName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CredRead(string targetName, uint type, uint flags, out Credential* credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CredWrite(Credential* credential, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDelete(string targetName, uint type, uint flags);

    [LibraryImport("advapi32.dll")]
    private static unsafe partial void CredFree(void* buffer);
}
