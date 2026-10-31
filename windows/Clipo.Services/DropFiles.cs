using System.Buffers.Binary;
using System.Text;

namespace Clipo.Services;

/// <summary>The <c>CF_HDROP</c> clipboard format: a DROPFILES header and a list of paths.</summary>
public static class DropFiles
{
    private const int HeaderSize = 20;

    /// <summary>
    /// <paramref name="paths"/> as a DROPFILES block: UTF-16 paths, each
    /// NUL-terminated, with one more NUL at the end.
    /// </summary>
    public static byte[] Build(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var list = string.Concat(paths.Select(path => path + '\0')) + '\0';
        var block = new byte[HeaderSize + Encoding.Unicode.GetByteCount(list)];
        BinaryPrimitives.WriteUInt32LittleEndian(block, HeaderSize);
        // The point and "non-client" fields stay zero; the paths are wide characters.
        BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(16), 1);
        Encoding.Unicode.GetBytes(list, block.AsSpan(HeaderSize));
        return block;
    }
}
