using System.Buffers.Binary;

namespace Clipo.Services;

/// <summary>
/// The device-independent bitmap that <c>CF_DIB</c> carries: a BITMAPINFOHEADER
/// (or a later version of it) and pixels, but no file header.
/// </summary>
public static class Dib
{
    private const int FileHeaderSize = 14;
    private const int InfoHeaderSize = 40;
    private const uint BitFields = 3;
    private const uint AlphaBitFields = 6;

    /// <summary>The width and height in pixels, from the first bytes of a DIB. <c>false</c> if they are not a header.</summary>
    public static bool TryReadSize(ReadOnlySpan<byte> dib, out uint width, out uint height)
    {
        width = height = 0;
        if (dib.Length < 12 || !IsHeaderSize(BinaryPrimitives.ReadUInt32LittleEndian(dib)))
        {
            return false;
        }
        var w = BinaryPrimitives.ReadInt32LittleEndian(dib[4..]);
        var h = BinaryPrimitives.ReadInt32LittleEndian(dib[8..]);
        // A negative height means the rows are stored top-down.
        if (w <= 0 || h == 0 || h == int.MinValue)
        {
            return false;
        }
        width = (uint)w;
        height = (uint)Math.Abs(h);
        return true;
    }

    /// <summary>
    /// The DIB as the contents of a .bmp file, which image decoders read.
    /// <c>null</c> if <paramref name="dib"/> is not a DIB this understands.
    /// </summary>
    public static byte[]? ToBmpFile(ReadOnlySpan<byte> dib)
    {
        if (dib.Length < InfoHeaderSize || !IsHeaderSize(BinaryPrimitives.ReadUInt32LittleEndian(dib)))
        {
            return null;
        }
        var headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(dib);
        var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(dib[16..]);
        var colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(dib[32..]);

        long pixelsAt = FileHeaderSize + headerSize;
        // Only the plain 40-byte header has its color masks after it; later headers include them.
        if (headerSize == InfoHeaderSize)
        {
            pixelsAt += compression == BitFields ? 12 : compression == AlphaBitFields ? 16 : 0;
        }
        var colors = colorsUsed != 0 ? colorsUsed : bitCount <= 8 ? 1u << bitCount : 0;
        pixelsAt += 4L * colors;
        if (pixelsAt - FileHeaderSize > dib.Length)
        {
            return null;
        }

        var file = new byte[FileHeaderSize + dib.Length];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(2), (uint)file.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(10), (uint)pixelsAt);
        dib.CopyTo(file.AsSpan(FileHeaderSize));
        return file;
    }

    /// <summary>BITMAPINFOHEADER, BITMAPV4HEADER and BITMAPV5HEADER, and the two in-between sizes some apps write.</summary>
    private static bool IsHeaderSize(uint size) => size is 40 or 52 or 56 or 108 or 124;
}
