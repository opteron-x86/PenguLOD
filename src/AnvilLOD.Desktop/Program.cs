using Avalonia;

namespace AnvilLOD.Desktop;

internal static class Program
{
    internal static string? SettingsFile;
    internal static bool SmokeTest;
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--settings" && i + 1 < args.Length) SettingsFile = Path.GetFullPath(args[++i]);
                else if (args[i] == "--smoke-test") SmokeTest = true;
                else if (args[i] is "--help" or "-h")
                { Console.WriteLine("AnvilLOD.Desktop [--settings /path/to/settings.json] [--smoke-test]"); return 0; }
                else throw new ArgumentException("Unknown or incomplete argument: " + args[i]);
            }
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("AnvilLOD could not start: " + ex.Message);
            if (ex.Message.Contains("XOpenDisplay", StringComparison.Ordinal))
                Console.Error.WriteLine("Start the app from a terminal in your graphical desktop session. It needs access to DISPLAY and XAUTHORITY; Wayland sessions also need XWayland.");
            else if (ex is not ArgumentException) Console.Error.WriteLine(ex);
            return 1;
        }
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
