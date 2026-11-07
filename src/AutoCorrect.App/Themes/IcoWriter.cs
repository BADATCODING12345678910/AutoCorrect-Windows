using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace AutoCorrect.App.Themes;

/// <summary>
/// Writes a classic BMP-style .ico (more reliable for Explorer shortcut icons than PNG-in-ICO).
/// </summary>
internal static class IcoWriter
{
    public static void SaveIcon(Bitmap source, string path, params int[] sizes)
    {
        if (sizes is null || sizes.Length == 0)
            sizes = [16, 32, 48, 256];

        var images = new List<byte[]>(sizes.Length);
        foreach (var size in sizes.Distinct().OrderBy(s => s))
        {
            using var scaled = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(scaled))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, 0, 0, size, size);
            }
            images.Add(EncodeBmpIconImage(scaled));
        }

        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((ushort)0);
            bw.Write((ushort)1);
            bw.Write((ushort)images.Count);

            var offset = 6 + (16 * images.Count);
            foreach (var img in images)
            {
                var dim = GuessSize(img);
                bw.Write((byte)(dim >= 256 ? 0 : dim));
                bw.Write((byte)(dim >= 256 ? 0 : dim));
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((ushort)1);
                bw.Write((ushort)32);
                bw.Write(img.Length);
                bw.Write(offset);
                offset += img.Length;
            }

            foreach (var img in images)
                bw.Write(img);
        }

        File.Copy(tmp, path, overwrite: true);
        try { File.Delete(tmp); } catch { /* ignore */ }
    }

    private static int GuessSize(byte[] dib)
    {
        // BITMAPINFOHEADER biWidth at offset 4
        if (dib.Length < 8)
            return 32;
        return Math.Abs(BitConverter.ToInt32(dib, 4));
    }

    private static byte[] EncodeBmpIconImage(Bitmap bmp)
    {
        var w = bmp.Width;
        var h = bmp.Height;
        var data = bmp.LockBits(
            new Rectangle(0, 0, w, h),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var pixels = new byte[stride * h];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            // XOR bitmap is bottom-up BGRA; AND mask follows (1 bit/pixel, padded).
            var xor = new byte[stride * h];
            for (var y = 0; y < h; y++)
            {
                var srcRow = y * stride;
                var dstRow = (h - 1 - y) * stride;
                Buffer.BlockCopy(pixels, srcRow, xor, dstRow, stride);
            }

            var maskStride = ((w + 31) / 32) * 4;
            var andMask = new byte[maskStride * h]; // opaque → 0 bits

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            // BITMAPINFOHEADER
            bw.Write(40);
            bw.Write(w);
            bw.Write(h * 2); // height includes AND mask
            bw.Write((ushort)1);
            bw.Write((ushort)32);
            bw.Write(0); // BI_RGB
            bw.Write(xor.Length);
            bw.Write(0);
            bw.Write(0);
            bw.Write(0);
            bw.Write(0);
            bw.Write(xor);
            bw.Write(andMask);
            return ms.ToArray();
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
