using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AutoBlend.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
    }

    // WPF doesn't follow the OS dark mode setting for its own native title bar — without this,
    // the window chrome renders with Windows' default light title bar around our dark content,
    // which reads as a stray white frame.
    private void ApplyDarkTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var useDarkMode = 1;
            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeLegacy, ref useDarkMode, sizeof(int));
            }
        }
        catch
        {
            // cosmetic only — never worth crashing the app over
        }
    }
}
