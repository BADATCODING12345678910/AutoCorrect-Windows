using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Platform;
using AvBitmap = Avalonia.Media.Imaging.Bitmap;
using SdColor = System.Drawing.Color;
using SdPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace AutoCorrect.App.Themes;

/// <summary>
/// Recolors the branded squircle logo so background + accent follow the active theme
/// while preserving the “A” glyph shape.
/// </summary>
public static class ThemedLogoFactory
{
    private const string AssetPath = "avares://AutoCorrect.App/Assets/autocorrect-icon.jpg";
    private static readonly object Gate = new();
    private static byte[]? _srcBgra;
    private static int _width;
    private static int _height;

    public static AvBitmap CreateAvaloniaBitmap(AppThemePalette theme, int maxEdge = 256)
    {
        // Decode fully before disposing the stream (Avalonia may keep a stream reference).
        var stream = CreatePngStream(theme, maxEdge);
        try
        {
            return new AvBitmap(stream);
        }
        finally
        {
            // Bitmap constructor reads the whole stream synchronously for PNG.
            stream.Dispose();
        }
    }

    public static MemoryStream CreatePngStream(
        AppThemePalette theme,
        int maxEdge = 256,
        bool privacyBadge = false)
    {
        using var themed = CreateSystemBitmap(theme, maxEdge, privacyBadge);
        var ms = new MemoryStream();
        themed.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        return ms;
    }

    public static System.Drawing.Bitmap CreateSystemBitmap(
        AppThemePalette theme,
        int maxEdge = 256,
        bool privacyBadge = false)
    {
        EnsureSourceLoaded();
        var w = _width;
        var h = _height;
        var src = _srcBgra!;

        var scale = 1;
        if (maxEdge > 0 && Math.Max(w, h) > maxEdge)
            scale = (int)Math.Ceiling(Math.Max(w, h) / (double)maxEdge);

        var outW = Math.Max(1, w / scale);
        var outH = Math.Max(1, h / scale);
        var bmp = new System.Drawing.Bitmap(outW, outH, SdPixelFormat.Format32bppArgb);

        var top = ToSd(theme.Black);
        var bottom = ToSd(theme.WindowGradientEnd ?? theme.AccentDeep);
        var accent = ToSd(theme.Accent);
        var accentSoft = ToSd(theme.AccentSoft);
        var glyph = theme.IsDark
            ? SdColor.FromArgb(255, 255, 255)
            : ToSd(theme.Text);

        var data = bmp.LockBits(
            new Rectangle(0, 0, outW, outH),
            ImageLockMode.WriteOnly,
            SdPixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var row = new byte[stride];
            for (var y = 0; y < outH; y++)
            {
                var srcY = Math.Min(h - 1, y * scale);
                Array.Clear(row);
                for (var x = 0; x < outW; x++)
                {
                    var srcX = Math.Min(w - 1, x * scale);
                    var si = (srcY * w + srcX) * 4;
                    var color = MapPixel(
                        src[si + 2], src[si + 1], src[si], src[si + 3],
                        srcY / (float)Math.Max(1, h - 1),
                        top, bottom, accent, accentSoft, glyph);

                    var di = x * 4;
                    row[di] = color.B;
                    row[di + 1] = color.G;
                    row[di + 2] = color.R;
                    row[di + 3] = color.A;
                }

                Marshal.Copy(row, 0, data.Scan0 + y * stride, stride);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        if (privacyBadge)
            DrawPrivacyBadge(bmp);

        return bmp;
    }

    /// <summary>Bottom-right teal shield so tray shows Privacy mode at a glance.</summary>
    public static void DrawPrivacyBadge(System.Drawing.Bitmap bmp)
    {
        var size = Math.Max(bmp.Width, bmp.Height);
        var r = Math.Max(4, size / 5);
        var cx = bmp.Width - r - Math.Max(1, size / 16);
        var cy = bmp.Height - r - Math.Max(1, size / 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var ring = new SolidBrush(SdColor.FromArgb(255, 255, 255, 255)))
            g.FillEllipse(ring, cx - r, cy - r, r * 2, r * 2);
        using (var fill = new SolidBrush(SdColor.FromArgb(255, 45, 212, 191)))
            g.FillEllipse(fill, cx - r + 2, cy - r + 2, r * 2 - 4, r * 2 - 4);
        // Simple lock body
        var lockW = Math.Max(3, r / 2);
        var lockH = Math.Max(3, r / 2);
        var lx = cx - lockW / 2;
        var ly = cy - lockH / 4;
        using (var lockBrush = new SolidBrush(SdColor.FromArgb(255, 15, 23, 42)))
        {
            g.FillRectangle(lockBrush, lx, ly, lockW, lockH);
            using var pen = new Pen(SdColor.FromArgb(255, 15, 23, 42), Math.Max(1.5f, r / 6f));
            g.DrawArc(pen, lx, ly - lockH / 2f, lockW, lockH, 180, 180);
        }
    }

    private static SdColor MapPixel(
        byte r, byte g, byte b, byte a,
        float yNorm,
        SdColor top, SdColor bottom, SdColor accent, SdColor accentSoft, SdColor glyph)
    {
        if (a < 8)
            return SdColor.FromArgb(0, 0, 0, 0);

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var lum = (0.2126f * r + 0.7152f * g + 0.0722f * b) / 255f;
        var sat = max == 0 ? 0f : delta / (float)max;

        // Near-white glyph (“A”)
        if (lum > 0.78f && sat < 0.22f)
            return SdColor.FromArgb(a, glyph.R, glyph.G, glyph.B);

        // Bright purple sparkle / crescent → theme accent
        if (sat > 0.28f && lum is > 0.22f and < 0.85f && r >= g && b >= g * 0.55f)
        {
            var t = Math.Clamp((lum - 0.22f) / 0.55f, 0f, 1f);
            return SdColor.FromArgb(a,
                Lerp(accent.R, accentSoft.R, t),
                Lerp(accent.G, accentSoft.G, t),
                Lerp(accent.B, accentSoft.B, t));
        }

        // Background gradient tinted to the theme.
        var shade = Math.Clamp(lum * 1.35f, 0f, 1f);
        var baseCol = LerpColor(top, bottom, Math.Clamp(yNorm * 0.85f + shade * 0.15f, 0f, 1f));
        var lifted = LerpColor(baseCol, accent, shade * 0.22f);
        return SdColor.FromArgb(a, lifted.R, lifted.G, lifted.B);
    }

    private static SdColor LerpColor(SdColor a, SdColor b, float t) =>
        SdColor.FromArgb(
            Lerp(a.A, b.A, t),
            Lerp(a.R, b.R, t),
            Lerp(a.G, b.G, t),
            Lerp(a.B, b.B, t));

    private static byte Lerp(byte a, byte b, float t) =>
        (byte)Math.Clamp(a + (b - a) * t, 0, 255);

    private static SdColor ToSd(Avalonia.Media.Color c) =>
        SdColor.FromArgb(c.A, c.R, c.G, c.B);

    private static void EnsureSourceLoaded()
    {
        if (_srcBgra is not null)
            return;

        lock (Gate)
        {
            if (_srcBgra is not null)
                return;

            using var stream = AssetLoader.Open(new Uri(AssetPath));
            using var src = new System.Drawing.Bitmap(stream);
            _width = src.Width;
            _height = src.Height;
            var rect = new Rectangle(0, 0, _width, _height);
            var data = src.LockBits(rect, ImageLockMode.ReadOnly, SdPixelFormat.Format32bppArgb);
            try
            {
                var stride = Math.Abs(data.Stride);
                var buffer = new byte[stride * _height];
                Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
                if (stride != _width * 4)
                {
                    var tight = new byte[_width * _height * 4];
                    for (var y = 0; y < _height; y++)
                        Buffer.BlockCopy(buffer, y * stride, tight, y * _width * 4, _width * 4);
                    _srcBgra = tight;
                }
                else
                {
                    _srcBgra = buffer;
                }
            }
            finally
            {
                src.UnlockBits(data);
            }
        }
    }
}
