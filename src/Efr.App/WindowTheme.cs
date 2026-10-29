using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Efr.App;

internal static class WindowTheme
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    public static void Apply(Window window)
    {
        int dark = 1;
        _ = DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref dark, sizeof(int));
    }
}
