using System.Buffers.Binary;
using System.Text;
using Clipo.Services;

namespace Clipo.Tests;

public sealed class ClipboardFormatTests
{
    [Fact]
    public void CfHtmlGivesTheMarkedFragment()
    {
        const string Fragment = "<b>سلام</b>";
        var data = CfHtmlSample(Fragment);

        Assert.Equal(Fragment, CfHtml.ExtractFragment(data));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Version:0.9\r\nStartHTML:0000000000\r\n")]
    [InlineData("StartFragment:0000000900\r\nEndFragment:0000000100\r\n")]
    [InlineData("StartFragment:0000000010\r\nEndFragment:0000009999\r\n")]
    [InlineData("StartFragment:abc\r\nEndFragment:def\r\n")]
    public void CfHtmlWithoutAValidFragmentGivesNull(string header)
    {
        Assert.Null(CfHtml.ExtractFragment(Encoding.UTF8.GetBytes(header)));
    }

    [Fact]
    public void BlankCfHtmlFragmentGivesNull()
    {
        Assert.Null(CfHtml.ExtractFragment(CfHtmlSample("   ")));
    }

    [Fact]
    public void DibSizeIsReadFromTheHeader()
    {
        Assert.True(Dib.TryReadSize(Header(width: 30, height: 20, bitCount: 24), out var width, out var height));
        Assert.Equal((30u, 20u), (width, height));
    }

    [Fact]
    public void TopDownDibHasAPositiveHeight()
    {
        Assert.True(Dib.TryReadSize(Header(width: 30, height: -20, bitCount: 24), out _, out var height));
        Assert.Equal(20u, height);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-5, 10)]
    [InlineData(10, 0)]
    public void DibWithoutAreaIsRejected(int width, int height)
    {
        Assert.False(Dib.TryReadSize(Header(width, height, bitCount: 24), out _, out _));
    }

    [Fact]
    public void DibSizeNeedsAHeader()
    {
        Assert.False(Dib.TryReadSize(new byte[8], out _, out _));
        Assert.False(Dib.TryReadSize(new byte[64], out _, out _));
    }

    [Fact]
    public void BmpFileWrapsA24BitDib()
    {
        var dib = Header(width: 2, height: 2, bitCount: 24).Concat(new byte[16]).ToArray();

        var file = Dib.ToBmpFile(dib);

        Assert.NotNull(file);
        Assert.Equal("BM", Encoding.ASCII.GetString(file[..2]));
        Assert.Equal((uint)file.Length, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(2)));
        Assert.Equal(14u + 40, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(10)));
        Assert.Equal(dib, file[14..]);
    }

    [Fact]
    public void BmpFileSkipsTheColorMasksOfABitfieldDib()
    {
        var dib = Header(width: 2, height: 2, bitCount: 32, compression: 3).Concat(new byte[12 + 16]).ToArray();

        var file = Dib.ToBmpFile(dib);

        Assert.NotNull(file);
        Assert.Equal(14u + 40 + 12, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(10)));
    }

    [Fact]
    public void BmpFileSkipsThePaletteOfAnIndexedDib()
    {
        var dib = Header(width: 2, height: 2, bitCount: 8).Concat(new byte[256 * 4 + 8]).ToArray();

        var file = Dib.ToBmpFile(dib);

        Assert.NotNull(file);
        Assert.Equal(14u + 40 + 256 * 4, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(10)));
    }

    [Fact]
    public void BmpFileRejectsATruncatedPalette()
    {
        Assert.Null(Dib.ToBmpFile(Header(width: 2, height: 2, bitCount: 8)));
    }

    [Fact]
    public void BmpFileRejectsDataThatIsNoDib()
    {
        Assert.Null(Dib.ToBmpFile(new byte[100]));
        Assert.Null(Dib.ToBmpFile(new byte[10]));
    }

    [Theory]
    [InlineData("ExcludeClipboardContentFromMonitorProcessing", true)]
    [InlineData("excludeclipboardcontentfrommonitorprocessing", true)]
    [InlineData("Clipboard Viewer Ignore", true)]
    [InlineData("HTML Format", false)]
    [InlineData("CanIncludeInClipboardHistory", false)]
    public void ConcealedFormatsAreRecognizedByName(string name, bool concealed)
    {
        Assert.Equal(concealed, ConcealedFormats.IsConcealed(["Rich Text Format", name]));
    }

    [Fact]
    public void NoFormatsAreNotConcealed()
    {
        Assert.False(ConcealedFormats.IsConcealed([]));
    }

    [Theory]
    [InlineData(0u, true)]
    [InlineData(1u, false)]
    public void HistoryOptOutIsAZeroDword(uint value, bool optsOut)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);

        Assert.Equal(optsOut, ConcealedFormats.OptsOutOfHistory(bytes));
    }

    [Fact]
    public void HistoryOptOutNeedsAWholeDword()
    {
        Assert.False(ConcealedFormats.OptsOutOfHistory(new byte[2]));
    }

    [Fact]
    public void HotkeyIsWrittenLikeAShortcut()
    {
        Assert.Equal("Ctrl+Shift+V", HotkeyChord.ShowHistory.Display);
        Assert.Equal("Ctrl+Alt+Win+7", new HotkeyChord(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Windows, '7').Display);
        Assert.Equal("Alt+0x70", new HotkeyChord(HotkeyModifiers.Alt, 0x70).Display);
    }

    /// <summary>"HTML Format" with its header: offsets are bytes into the whole data.</summary>
    private static byte[] CfHtmlSample(string fragment)
    {
        const string Prefix = "<html><body><!--StartFragment-->";
        const string Suffix = "<!--EndFragment--></body></html>";
        string Header(int start, int end) =>
            $"Version:0.9\r\nStartHTML:0000000000\r\nEndHTML:0000000000\r\nStartFragment:{start:D10}\r\nEndFragment:{end:D10}\r\n";
        var headerLength = Encoding.UTF8.GetByteCount(Header(0, 0));
        var start = headerLength + Encoding.UTF8.GetByteCount(Prefix);
        var end = start + Encoding.UTF8.GetByteCount(fragment);
        return Encoding.UTF8.GetBytes(Header(start, end) + Prefix + fragment + Suffix + "\0");
    }

    /// <summary>A BITMAPINFOHEADER.</summary>
    private static byte[] Header(int width, int height, ushort bitCount, uint compression = 0)
    {
        var header = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(header, 40);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), height);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), bitCount);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), compression);
        return header;
    }
}
