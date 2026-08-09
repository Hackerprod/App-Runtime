using System.Buffers.Binary;
using System.IO;
using AndroidRuntime.WindowsHost;

namespace AndroidRuntime.WindowsHost.Tests;

/// <summary>
/// Phase-2 presentation-tooling test: the BGRA32 top-down BMP writer is the
/// capture/presentation utility that survives (it writes ViewRuntime's finished
/// pixel buffer to disk). The old native-rasterizer test is REMOVED along with
/// the Phase-1 command-interpretation path — this side no longer rasterizes.
/// </summary>
public sealed class ViewRuntimeIntegrationTests
{
    [Fact]
    public void Bmp_writer_produces_a_valid_top_down_bgra32_file()
    {
        var capture = new WindowsFrameCapture(2, 2,
        [
            // 2x2 BGRA: red, green / blue, white
            0, 0, 255, 255,  0, 255, 0, 255,
            255, 0, 0, 255,  255, 255, 255, 255
        ], "sha", 1, "snap");
        string path = Path.Combine(Path.GetTempPath(), "vr-test-" + Guid.NewGuid().ToString("N") + ".bmp");
        try
        {
            BmpFrameWriter.Write(path, capture);
            byte[] bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length >= 54 + 16, "file is at least header + 2x2 pixels");

            // BITMAPFILEHEADER: 'BM', bfSize at offset 2, bfOffBits at offset 10.
            Assert.Equal((byte)'B', bytes[0]);
            Assert.Equal((byte)'M', bytes[1]);
            Assert.Equal(54 + 16, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(2, 4)));
            Assert.Equal(54, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(10, 4)));
            // BITMAPINFOHEADER: 40-byte size, 2x2, negative height (top-down), 32bpp.
            Assert.Equal(40, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(14, 4)));
            Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18, 4)));
            Assert.Equal(-2, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22, 4)));
            Assert.Equal(32, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(28, 2)));
            // Pixel bytes at offset 54, top-down: [red, green / blue, white].
            Assert.Equal((byte)0, bytes[54]); Assert.Equal((byte)0, bytes[55]); Assert.Equal((byte)255, bytes[56]); Assert.Equal((byte)255, bytes[57]);
            Assert.Equal((byte)0, bytes[58]); Assert.Equal((byte)255, bytes[59]); Assert.Equal((byte)0, bytes[60]); Assert.Equal((byte)255, bytes[61]);
            Assert.Equal((byte)255, bytes[62]); Assert.Equal((byte)0, bytes[63]); Assert.Equal((byte)0, bytes[64]); Assert.Equal((byte)255, bytes[65]);
            Assert.Equal((byte)255, bytes[66]); Assert.Equal((byte)255, bytes[67]); Assert.Equal((byte)255, bytes[68]); Assert.Equal((byte)255, bytes[69]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
