using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Reticle.Core;
using Screen = System.Windows.Forms.Screen;

namespace Reticle.App;

public sealed class OverlayWindow : Window
{
    private readonly CrosshairSettings settings;
    private IntPtr handle;
    public IntPtr Handle => handle;
    public OverlayWindow(CrosshairSettings settings)
    {
        this.settings = settings;
        Title = "Reticle Overlay"; Width = 400; Height = 400;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true;
        ShowInTaskbar = false; ShowActivated = false; Focusable = false; IsHitTestVisible = false;
        Content = new CrosshairView { Settings = settings, PhysicalPixels = true, IsHitTestVisible = false };
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
        return IntPtr.Zero;
    }
    public Screen SelectedScreen => Screen.AllScreens.FirstOrDefault(s => s.DeviceName == settings.Monitor) ?? Screen.PrimaryScreen ?? Screen.AllScreens[0];
    public void Position()
    {
        if (handle == IntPtr.Zero) return;
        var bounds = SelectedScreen.Bounds;
        const int size = 400;
        int x = bounds.Left + bounds.Width / 2 - size / 2 + (int)Math.Round(settings.OffsetX);
        int y = bounds.Top + bounds.Height / 2 - size / 2 + (int)Math.Round(settings.OffsetY);
        if (!NativeMethods.SetWindowPos(handle, new IntPtr(-1), x, y, size, size, NativeMethods.NoActivatePosition))
            throw new System.ComponentModel.Win32Exception();
    }
    public void SetVisible(bool visible)
    {
        if (visible) { Show(); Position(); }
        else Hide();
    }
}
