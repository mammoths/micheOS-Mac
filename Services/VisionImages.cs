using System.Buffers.Binary;
using Avalonia.Media.Imaging;

namespace Miche.Mac.Services;

public static class VisionImages
{
    public static Guid Import(WorkspaceStore store, Guid micheId, string source, double left, double top, Guid? artifactId=null)
    {
        var length = new FileInfo(source).Length;
        if (length is < 8 or > 20971520) throw new ArgumentException("Use a PNG or JPEG image under 20 MB.");
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var buffer = new MemoryStream((int)Math.Min(length,20971520));
        var chunk = new byte[8192]; int count;
        while ((count = input.Read(chunk)) > 0)
        {
            if (buffer.Length + count > 20971520) throw new ArgumentException("Image exceeds 20 MB.");
            buffer.Write(chunk,0,count);
        }
        var bytes = buffer.ToArray();
        if (bytes.Length < 8) throw new ArgumentException("Image is incomplete.");
        var (extension, width, height) = Dimensions(bytes);
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192 || (long)width * height > 32000000)
            throw new ArgumentException("Use an image up to 8192 px per side and 32 megapixels.");
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var bitmap = new Bitmap(stream);
            if (bitmap.PixelSize.Width != width || bitmap.PixelSize.Height != height) throw new ArgumentException("Image dimensions are invalid.");
        }
        catch (Exception e) when (e is not ArgumentException) { throw new ArgumentException("This image could not be decoded.", e); }
        return store.AddVisionImage(micheId, bytes, extension, width, height, Math.Clamp(left,0,10000), Math.Clamp(top,0,10000),artifactId);
    }
    private static (string Extension, int Width, int Height) Dimensions(byte[] bytes)
    {
        if (bytes.Length >= 24 && bytes.AsSpan(0,8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) &&
            bytes.AsSpan(12,4).SequenceEqual("IHDR"u8))
            return ("png", BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4)), BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4)));
        if (bytes[0] == 255 && bytes[1] == 216)
        {
            var p = 2;
            while (p + 4 <= bytes.Length)
            {
                if (bytes[p++] != 255) break;
                while (p < bytes.Length && bytes[p] == 255) p++;
                if (p >= bytes.Length) break;
                var marker = bytes[p++];
                if (marker is 0xD9 or 0xDA) break;
                if (marker is 0x01 or >= 0xD0 and <= 0xD7) continue;
                if (p + 2 > bytes.Length) break;
                var size = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(p,2));
                if (size < 2 || p + size > bytes.Length) break;
                if (marker is 0xC0 or 0xC1 or 0xC2 && size >= 8)
                    return ("jpg", BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(p+5,2)), BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(p+3,2)));
                p += size;
            }
        }
        throw new ArgumentException("Only valid PNG and JPEG images are supported in this preview.");
    }
}
