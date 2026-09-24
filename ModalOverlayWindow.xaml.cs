using System.Windows;
using System.Windows.Input;
using WinForms = System.Windows.Forms;

namespace GmailCalendarNotifier;

/// <summary>
/// Full-screen semi-transparent overlay shown behind the reminder popup on each monitor.
/// Swallows all input so no other application can be used until the reminders are handled;
/// clicking it (or trying to switch away) pushes focus back to the popup.
/// The popup window takes this overlay as its Owner, which keeps the popup rendered
/// above the overlay (owned windows always render above their owner).
/// </summary>
public partial class ModalOverlayWindow : Window
{
    private readonly ReminderWindow _popup;

    public ModalOverlayWindow(WinForms.Screen screen, ReminderWindow popup)
    {
        InitializeComponent();
        _popup = popup;

        // Cover the work area exactly — convert physical pixels to WPF DIPs first.
        var area = screen.WorkingArea;
        double sx = ScreenDpi.ScaleX(screen);
        double sy = ScreenDpi.ScaleY(screen);
        Left = area.Left / sx;
        Top = area.Top / sy;
        Width = area.Width / sx;
        Height = area.Height / sy;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _popup.Activate();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        // Someone tried to switch away while reminders are pending — keep them in front.
        if (_popup.IsVisible) _popup.Activate();
    }
}
