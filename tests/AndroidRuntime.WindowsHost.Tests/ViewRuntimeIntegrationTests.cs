using System.Buffers.Binary;
using System.IO;
using AndroidRuntime.Core.Ui;
using AndroidRuntime.WindowsHost;

namespace AndroidRuntime.WindowsHost.Tests;

/// <summary>
/// Tests for the ViewRuntime integration: the BGRA32 top-down BMP writer and
/// the native rasterizer path in WindowsRetainedRenderer (when the DLL is
/// available). The BMP test is deterministic; the native-render test asserts
/// the frame is a real, non-trivial BGRA buffer (not the plain background).
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

    [Fact]
    public void Native_rasterizer_renders_a_non_trivial_frame_when_dll_is_available()
    {
        using var renderer = new WindowsRetainedRenderer();
        renderer.Resize(320, 240, 1f);
        renderer.Render(Frame(1, "Ready"));

        WindowsFrameCapture capture = renderer.Capture();
        Assert.True(capture.Width == 320 && capture.Height == 240);
        Assert.True(capture.Bgra.Length == 320 * 240 * 4);

        // The frame must NOT be the plain uniform background: count distinct
        // 32-bit pixel values; a real render has the fill rect + text regions.
        var seen = new HashSet<uint>();
        for (int i = 0; i + 3 < capture.Bgra.Length; i += 4)
            seen.Add(BitConverter.ToUInt32(capture.Bgra, i));
        Assert.True(seen.Count >= 2, $"expected a non-trivial frame, saw {seen.Count} distinct pixel values");
    }

    private static WindowsRetainedFrame Frame(long revision, string text) => new(
        revision,
        320,
        240,
        1,
        [
            new AndroidFillRectCommand(new AndroidRect(20, 20, 100, 50), new AndroidColor(255, 35, 91, 180), 42),
            new AndroidDrawTextCommand(new AndroidRect(20, 20, 100, 50), text, 18, new AndroidColor(255, 255, 255, 255), 42)
        ],
        $"button|42|{text}");
}
