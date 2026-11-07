using System.Runtime.Versioning;
using Avalonia;
using FeatureDebugLab.ViewModels;

namespace FeatureDebugLab;

[SupportedOSPlatform("windows")]
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Headless offline bug hunt for agents/CI:
        //   FeatureDebugLab.exe --offline
        // Exit: 0 = clean, 1 = warnings only, 2 = failures
        if (args.Any(a => string.Equals(a, "--offline", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(a, "--headless", StringComparison.OrdinalIgnoreCase)))
        {
            return MainViewModel.RunHeadlessOffline();
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
