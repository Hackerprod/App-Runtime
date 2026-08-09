#nullable enable
using System.Buffers.Binary;
using System.IO;

namespace AndroidRuntime.WindowsHost;

/// <summary>
/// Minimal BGRA32 top-down BMP writer with NO external dependency (no
/// System.Drawing.Common). `WindowsFrameCapture.Bgra` is already the exact
/// byte layout a 32-bpp top-down BMP stores — rows are BGRA, stride is
/// width*4 (32bpp needs no row padding), and a negative biHeight marks a
/// top-down bitmap so the first byte is the top-left pixel. This is the
/// smallest transformation from what Capture() already produces.
/// </summary>
internal static class BmpFrameWriter
{
    internal static void Write(string path, WindowsFrameCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        int width = capture.Width, height = capture.Height;
        int stride = checked(width * 4);
        int pixelBytes = checked(stride * height);
        int fileSize = checked(14 + 40 + pixelBytes); // BITMAPFILEHEADER + BITMAPINFOHEADER + pixels

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream);

        // BITMAPFILEHEADER (14 bytes).
        Span<byte> header = stackalloc byte[14];
        // bfType 'BM'
        header[0] = (byte)'B'; header[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(header[2..], fileSize);
        // bfReserved1, bfReserved2 = 0
        BinaryPrimitives.WriteInt32LittleEndian(header[6..], 0);
        // bfOffBits = 14 + 40 = 54
        BinaryPrimitives.WriteInt32LittleEndian(header[10..], 54);
        writer.Write(header);

        // BITMAPINFOHEADER (40 bytes).
        Span<byte> info = stackalloc byte[40];
        BinaryPrimitives.WriteInt32LittleEndian(info[0..], 40);      // biSize
        BinaryPrimitives.WriteInt32LittleEndian(info[4..], width);   // biWidth
        BinaryPrimitives.WriteInt32LittleEndian(info[8..], -height); // biHeight (negative = top-down)
        BinaryPrimitives.WriteInt16LittleEndian(info[12..], 1);      // biPlanes
        BinaryPrimitives.WriteInt16LittleEndian(info[14..], 32);     // biBitCount
        BinaryPrimitives.WriteInt32LittleEndian(info[16..], 0);      // biCompression = BI_RGB
        BinaryPrimitives.WriteInt32LittleEndian(info[20..], pixelBytes); // biSizeImage
        BinaryPrimitives.WriteInt32LittleEndian(info[24..], 0);      // biXPelsPerMeter
        BinaryPrimitives.WriteInt32LittleEndian(info[28..], 0);      // biYPelsPerMeter
        BinaryPrimitives.WriteInt32LittleEndian(info[32..], 0);      // biClrUsed
        BinaryPrimitives.WriteInt32LittleEndian(info[36..], 0);      // biClrImportant
        writer.Write(info);

        // Pixel data: BGRA, top-down, stride = width*4 (32bpp has no padding).
        writer.Write(capture.Bgra, 0, pixelBytes);
        writer.Flush();
    }
}
