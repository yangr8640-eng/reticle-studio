using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Reticle.Core;
using Screen = System.Windows.Forms.Screen;

namespace Reticle.App;

public sealed class OverlayWindow : Window
{
    private readonly CrosshairSettings settings;
    private readonly DispatcherTimer recoveryTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(500)
    };
    private IntPtr handle;
    private bool refreshQueued;
    public IntPtr Handle => handle;
    public OverlayWindow(CrosshairSettings settings)
    {
        this.settings = settings;
        Title = "Reticle Overlay"; Width = 400; Height = 400;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true;
        ShowInTaskbar = false; ShowActivated = false; Focusable = false; IsHitTestVisible = false;
        Content = new CrosshairView { Settings = settings, PhysicalPixels = true, IsHitTestVisible = false };
        // A game can enter the topmost group after us, including after Alt+Tab.
        // Reassert our z-order without activating, hiding, or recreating the HWND.
        recoveryTimer.Tick += (_, _) => TryPosition();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) recoveryTimer.Start();
            else recoveryTimer.Stop();
        };
        Closed += (_, _) =>
        {
            recoveryTimer.Stop();
            // Game Bar-only sessions never create a desktop HWND.
            if (handle != IntPtr.Zero) HwndSource.FromHwnd(handle)?.RemoveHook(Hook);
            handle = IntPtr.Zero;
        };
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            int styles = NativeMethods.GetWindowLong(handle, NativeMethods.GwlExStyle);
            NativeMethods.SetWindowLong(handle, NativeMethods.GwlExStyle, styles | NativeMethods.Transparent | NativeMethods.ToolWindow | NativeMethods.NoActivate | NativeMethods.Layered);
            HwndSource.FromHwnd(handle)?.AddHook(Hook);
        };
    }
    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmNcHitTest) { handled = true; return new IntPtr(-1); }
        if (msg == NativeMethods.WmMouseActivate) { handled = true; return new IntPtr(3); }
        if (msg is NativeMethods.WmDisplayChange or NativeMethods.WmDpiChanged && !refreshQueued)
        {
            // Let WPF finish processing the mode/DPI change before restoring physical pixels.
            refreshQueued = true;
            Dispatcher.BeginInvoke(() =>
            {
                refreshQueued = false;
                if (!IsVisible || handle == IntPtr.Zero) return;
                TryPosition();
                ((CrosshairView)Content).InvalidateVisual();
            }, DispatcherPriority.Background);
        }
        return IntPtr.Zero;
    }
    public Screen SelectedScreen => Screen.AllScreens.FirstOrDefault(s => s.DeviceName == settings.Monitor) ?? Screen.PrimaryScreen ?? Screen.AllScreens[0];
    public void Position()
    {
        if (!TryPosition()) throw new System.ComponentModel.Win32Exception();
    }
    private bool TryPosition()
    {
        if (handle == IntPtr.Zero || !IsVisible) return true;
        var bounds = SelectedScreen.Bounds;
        const int size = 400;
        int x = bounds.Left + bounds.Width / 2 - size / 2 + (int)Math.Round(settings.OffsetX);
        int y = bounds.Top + bounds.Height / 2 - size / 2 + (int)Math.Round(settings.OffsetY);
        uint flags = NativeMethods.NoActivatePosition | NativeMethods.NoOwnerZOrder;
        if (NativeMethods.GetWindowRect(handle, out var current))
        {
            if (current.Left == x && current.Top == y) flags |= NativeMethods.NoMove;
            if (current.Right - current.Left == size && current.Bottom - current.Top == size) flags |= NativeMethods.NoSize;
        }
        // A transient failure during a display transition is retried on the next tick.
        return NativeMethods.SetWindowPos(handle, new IntPtr(-1), x, y, size, size, flags);
    }
    public void SetVisible(bool visible)
    {
        if (visible) { Show(); Position(); }
        else Hide();
    }
}
