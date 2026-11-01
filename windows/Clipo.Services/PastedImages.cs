using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Clipo.Core;
using Clipo.Services.Native;

namespace Clipo.Services;

/// <summary>
/// Image files for pasting a picked image into a folder. Explorer and the
/// desktop paste files, not pictures, so an image picked over a folder goes
/// on the clipboard as a file too. Only the last file written is kept: it is
/// the one the clipboard points to. The folder holds decrypted copies of
/// history items, so Clipo empties it when it starts and quits.
/// </summary>
/// <param name="folder">Where the files go; created when needed.</param>
public sealed class PastedImages(string folder)
{
    /// <summary>Characters Windows does not allow in a file name, whatever system the tests run on.</summary>
    private static readonly HashSet<char> s_notInFileNames = [.. Path.GetInvalidFileNameChars(), '\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>A Clipo folder in the user's temporary folder.</summary>
    public static string DefaultFolder() => Path.Combine(Path.GetTempPath(), "Clipo");

    /// <summary>
    /// A file name for an image: <paramref name="baseName"/> and
    /// <paramref name="time"/>, as Windows names screenshots, with the
    /// extension of <paramref name="format"/>.
    /// </summary>
    public static string FileName(string baseName, ImageFormat format, DateTime time)
    {
        var extension = format switch
        {
            ImageFormat.Jpeg => "jpg",
            ImageFormat.Tiff => "tif",
            ImageFormat.Bmp => "bmp",
            _ => "png",
        };
        var name = $"{baseName} {time.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture)}.{extension}";
        // A translated base name must not bring a character a file name cannot have.
        return string.Concat(name.Select(c => s_notInFileNames.Contains(c) || char.IsControl(c) ? '_' : c));
    }

    /// <summary>
    /// Writes <paramref name="data"/> as <paramref name="fileName"/> and
    /// deletes the files written before. Returns the file's path as other
    /// apps see it, or <c>null</c> if it could not be written.
    /// </summary>
    public string? Save(byte[] data, string fileName)
    {
        ArgumentNullException.ThrowIfNull(data);
        try
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, Path.GetFileName(fileName));
            DeleteAll(except: path);
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                file.Write(data);
                return OperatingSystem.IsWindows() ? RealPath(file) ?? path : path;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"writing an image file for pasting failed: {e.GetType().Name}");
            return null;
        }
    }

    /// <summary>Deletes every file written; one in use is left.</summary>
    public void Clear() => DeleteAll(except: null);

    private void DeleteAll(string? except)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            if (string.Equals(file, except, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            try
            {
                File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Explorer may still be copying it; the next write or start tries again.
            }
        }
    }

    /// <summary>
    /// Where the file really is. A packaged Clipo's writes under
    /// <c>%LOCALAPPDATA%</c> go to a folder of its package, which Explorer
    /// would not find under the path Clipo used.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static unsafe string? RealPath(FileStream file)
    {
        const int MaxChars = 1024;
        var buffer = stackalloc char[MaxChars];
        var length = NativeMethods.GetFinalPathNameByHandle(file.SafeFileHandle.DangerousGetHandle(), buffer, MaxChars, 0);
        if (length == 0 || length >= MaxChars)
        {
            return null;
        }
        var path = new string(buffer, 0, (int)length);
        // \\?\C:\... and \\?\UNC\server\share\...
        if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
        {
            return @"\\" + path[8..];
        }
        return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
    }
}
