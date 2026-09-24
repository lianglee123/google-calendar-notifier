using System.Windows;
using System.Windows.Input;
using WinForms = System.Windows.Forms;

namespace GmailCalendarNotifier;

/// <summary>
/// Full-screen semi-transparent overlay shown behind the reminder popup on each monitor.
/// Swallows all input so no other application can be used until the reminders are handled;
/// clicking it pushes focus back to its owner (the reminder window).
/// </summary>
public partial class ModalOverlayWindow : Window
{
    public ModalOverlayWindow(WinForms.Screen screen, Window owner)
    {
        InitializeComponent();
        Owner = owner;

        var area = screen.WorkingArea;
        Left = area.Left;
        Top = area.Top;
        Width = area.Width;
        Height = area.Height;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Owner?.Activate();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        // Someone tried to switch away while reminders are pending — keep them in front.
        if (Owner is { IsVisible: true }) Owner.Activate();
    }
}
