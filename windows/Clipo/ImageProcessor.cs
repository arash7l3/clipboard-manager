using System.Diagnostics;
using Clipo.Core;
using Clipo.Services;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Clipo;

/// <summary>Measures, converts and shrinks images with the Windows Imaging Component.</summary>
internal sealed class ImageProcessor : IImageProcessor
{
    public async Task<ProcessedImage?> ProcessAsync(byte[] encoded, bool isPng, long maxPixels, int thumbnailPixels)
    {
        try
        {
            using var source = new InMemoryRandomAccessStream();
            await WriteAsync(source, encoded);
            var decoder = await BitmapDecoder.CreateAsync(source);
            var width = decoder.PixelWidth;
            var height = decoder.PixelHeight;
            if (width == 0 || height == 0 || (long)width * height > maxPixels)
            {
                return null;
            }

            var thumbnail = await ThumbnailAsync(decoder, thumbnailPixels);
            // A PNG is kept as it is; anything else, in practice a bitmap, becomes one.
            var data = isPng ? encoded : await EncodePngAsync(await FullPixelsAsync(decoder), width, height);
            return new ProcessedImage(width, height, ImageFormat.Png, data, thumbnail);
        }
        catch (Exception e)
        {
            // An image that does not decode is simply not kept. Never log its content.
            Trace.TraceWarning($"processing an image failed: {e.GetType().Name}: {e.Message}");
            return null;
        }
    }

    private static async Task<byte[]> FullPixelsAsync(BitmapDecoder decoder)
    {
        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        return pixels.DetachPixelData();
    }

    private static async Task<byte[]?> ThumbnailAsync(BitmapDecoder decoder, int longestSide)
    {
        var scale = Math.Min(1.0, (double)longestSide / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        var width = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale));
        var height = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale));
        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            new BitmapTransform { ScaledWidth = width, ScaledHeight = height, InterpolationMode = BitmapInterpolationMode.Fant },
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        return await EncodePngAsync(pixels.DetachPixelData(), width, height);
    }

    private static async Task<byte[]> EncodePngAsync(byte[] bgra, uint width, uint height)
    {
        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, width, height, 96, 96, bgra);
        await encoder.FlushAsync();
        return await ReadAllAsync(output);
    }

    private static async Task WriteAsync(InMemoryRandomAccessStream stream, byte[] data)
    {
        using var writer = new DataWriter(stream);
        writer.WriteBytes(data);
        await writer.StoreAsync();
        await writer.FlushAsync();
        writer.DetachStream();
        stream.Seek(0);
    }

    private static async Task<byte[]> ReadAllAsync(InMemoryRandomAccessStream stream)
    {
        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)bytes.Length);
        reader.ReadBytes(bytes);
        return bytes;
    }
}
