using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AutoBlend.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AutoBlend",
        "crash.log");

    public App()
    {
        // MO2 launches tools under USVFS, an injected DLL that hooks low-level Win32/NT calls.
        // WPF's default hardware-accelerated rendering initializes a D3D device very early —
        // under USVFS that path has produced an immediate 0xc0000005 access violation before any
        // managed code (not even this constructor) runs. Forcing software rendering avoids the
        // D3D/GPU-driver init path entirely.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);

        DispatcherUnhandledException += (_, e) =>
        {
            LogCrash("Dispatcher.UnhandledException", e.Exception);
            e.Handled = false;
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
            LogCrash("TaskScheduler.UnobservedTaskException", e.Exception);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            LogCrash("Startup", null, note: "OnStartup begin");
            base.OnStartup(e);
            LogCrash("Startup", null, note: "OnStartup end");
        }
        catch (Exception ex)
        {
            LogCrash("OnStartup", ex);
            throw;
        }
    }

    private static void LogCrash(string source, Exception? ex, string? note = null)
    {
        try
        {
            var directory = Path.GetDirectoryName(CrashLogPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(CrashLogPath,
                $"[{DateTime.Now:O}] [{source}] {note}{Environment.NewLine}{ex}{Environment.NewLine}---{Environment.NewLine}");
        }
        catch
        {
            // logging must never itself crash the app
        }
    }
}
