using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms;

namespace GmailCalendarNotifier;

/// <summary>
/// Converts WinForms Screen geometry (raw physical pixels) to WPF device-independent
/// units. Window.Left/Top/Width/Height are DIPs (96 DPI based) while Screen.WorkingArea
/// is in physical pixels — on scaled displays (125%/150%) feeding raw pixels to WPF
/// pushes windows towards the bottom-right and off-screen.
/// </summary>
public static class ScreenDpi
{
    public static double ScaleX(WinForms.Screen screen) => GetScale(screen).x;
    public static double ScaleY(WinForms.Screen screen) => GetScale(screen).y;

    private static (double x, double y) GetScale(WinForms.Screen screen)
    {
        try
        {
            var area = screen.WorkingArea;
            var pt = new POINT { X = area.Left + area.Width / 2, Y = area.Top + area.Height / 2 };
            IntPtr hmon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            if (hmon != IntPtr.Zero &&
                GetDpiForMonitor(hmon, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == 0)
            {
                return (dpiX / 96.0, dpiY / 96.0);
            }
        }
        catch { /* fall through to 1:1 */ }
        return (1.0, 1.0);
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
