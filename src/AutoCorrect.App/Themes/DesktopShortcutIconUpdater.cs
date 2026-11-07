using System.IO;
using System.Runtime.InteropServices;

namespace AutoCorrect.App.Themes;

/// <summary>
/// Points AutoCorrect desktop / Start Menu shortcuts at a themed .ico under LocalAppData.
/// Windows icon cache can lag; we write a per-theme filename and poke Explorer.
/// </summary>
public static class DesktopShortcutIconUpdater
{
    public static string IconDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoCorrect");

    public static string ThemedIconPathFor(string themeId) =>
        Path.Combine(IconDirectory, "themed-app-" + Sanitize(themeId) + ".ico");

    /// <summary>Legacy stable path (also written as a copy for older checks).</summary>
    public static string ThemedIconPath => Path.Combine(IconDirectory, "themed-app.ico");

    public static void Apply(AppThemePalette theme)
    {
        Directory.CreateDirectory(IconDirectory);
        var icoPath = ThemedIconPathFor(theme.Id);

        using (var bmp = ThemedLogoFactory.CreateSystemBitmap(theme, maxEdge: 256))
        {
            IcoWriter.SaveIcon(bmp, icoPath, 16, 32, 48, 256);
            // Keep legacy filename in sync for Feature Debug Lab / docs.
            try { File.Copy(icoPath, ThemedIconPath, overwrite: true); }
            catch { /* ignore lock */ }
        }

        var updated = 0;
        foreach (var lnk in FindAutoCorrectShortcuts())
        {
            if (TrySetShortcutIcon(lnk, icoPath))
            {
                updated++;
                try { File.SetLastWriteTimeUtc(lnk, DateTime.UtcNow); } catch { /* ignore */ }
                NotifyShellItemChanged(lnk);
            }
        }

        NotifyShellItemChanged(icoPath);
        NotifyShellItemChanged(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        if (updated == 0)
        {
            // Still notify desktop folder so Explorer re-reads icons if IconLocation was already set.
            NotifyShellItemChanged(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        }
    }

    private static string Sanitize(string themeId)
    {
        var s = (themeId ?? "theme").Trim().ToLowerInvariant();
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '-');
        return string.IsNullOrWhiteSpace(s) ? "theme" : s;
    }

    private static IEnumerable<string> FindAutoCorrectShortcuts()
    {
        var roots = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        };

        // OneDrive / redirected desktop
        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            roots.Add(Path.Combine(userProfile, "OneDrive", "Desktop"));
            roots.Add(Path.Combine(userProfile, "OneDrive", "桌面"));
        }
        catch { /* ignore */ }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*AutoCorrect*.lnk", SearchOption.AllDirectories);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                if (seen.Add(file))
                    yield return file;
            }
        }
    }

    private static bool TrySetShortcutIcon(string lnkPath, string icoPath)
    {
        try
        {
            if (Type.GetTypeFromProgID("WScript.Shell") is not { } shellType)
                return false;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic sc = shell.CreateShortcut(lnkPath);
            // Absolute path, index 0 — Explorer is picky about relative IconLocation.
            sc.IconLocation = Path.GetFullPath(icoPath) + ",0";
            sc.Save();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void NotifyShellItemChanged(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            var ptr = Marshal.StringToHGlobalUni(path);
            try
            {
                SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW | SHCNF_FLUSH, ptr, IntPtr.Zero);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch
        {
            /* ignore */
        }
    }

    private const uint SHCNE_UPDATEITEM = 0x00002000;
    private const uint SHCNF_PATHW = 0x0005;
    private const uint SHCNF_FLUSH = 0x1000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
