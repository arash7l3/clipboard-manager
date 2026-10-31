using Windows.Storage.Streams;

namespace Clipo;

/// <summary>Moves bytes in and out of WinRT streams, which the imaging APIs take.</summary>
internal static class Streams
{
    /// <summary>A stream holding <paramref name="data"/>, positioned at its start. The caller disposes it.</summary>
    public static async Task<InMemoryRandomAccessStream> FromBytesAsync(byte[] data)
    {
        var stream = new InMemoryRandomAccessStream();
        try
        {
            using var writer = new DataWriter(stream);
            writer.WriteBytes(data);
            await writer.StoreAsync();
            await writer.FlushAsync();
            // Disposing the writer would close the stream too.
            writer.DetachStream();
            stream.Seek(0);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public static async Task<byte[]> ToBytesAsync(InMemoryRandomAccessStream stream)
    {
        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)bytes.Length);
        reader.ReadBytes(bytes);
        return bytes;
    }
}
