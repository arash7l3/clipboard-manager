using System.Diagnostics;
using System.Runtime.Versioning;
using Clipo.Core;
using Clipo.Services.Native;

namespace Clipo.Services;

/// <summary>Finds the app that put something on the clipboard.</summary>
[SupportedOSPlatform("windows")]
public static unsafe class SourceAppResolver
{
    private const uint QueryLimitedInformation = 0x1000;

    /// <summary>
    /// The app that owns the clipboard right now, identified by its
    /// executable's file name (such as <c>KeePassXC.exe</c>). Windows records
    /// the owner, so this is exact unless the owner's window is already gone
    /// (a command-line tool that copied and exited); then the foreground app
    /// is credited. Call it as soon as the change is noticed. <c>null</c> if
    /// it cannot be found, for example for an elevated app.
    /// </summary>
    public static SourceApp? Current()
    {
        var window = NativeMethods.GetClipboardOwner();
        if (window == 0)
        {
            window = NativeMethods.GetForegroundWindow();
        }
        return window == 0 ? null : FromWindow(window);
    }

    private static SourceApp? FromWindow(nint window)
    {
        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0)
        {
            return null;
        }
        var process = NativeMethods.OpenProcess(QueryLimitedInformation, inherit: false, processId);
        if (process == 0)
        {
            return null;
        }
        try
        {
            var path = ImagePath(process);
            return path is null ? null : new SourceApp(Path.GetFileName(path), DisplayName(path));
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    private static string? ImagePath(nint process)
    {
        var buffer = stackalloc char[1024];
        var size = 1024u;
        return NativeMethods.QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
    }

    /// <summary>The name the app calls itself, such as "KeePassXC", or the file name without its extension.</summary>
    private static string DisplayName(string path)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (!string.IsNullOrWhiteSpace(description))
            {
                return description.Trim();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Fall back to the file name.
        }
        return Path.GetFileNameWithoutExtension(path);
    }
}
