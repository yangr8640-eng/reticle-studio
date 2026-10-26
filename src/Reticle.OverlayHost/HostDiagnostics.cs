using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Reticle.App;
using Reticle.Core;

namespace Reticle.OverlayHost;

internal static class HostDiagnostics
{
    internal static int Run(string outputPath)
    {
        var checks = new Dictionary<string, bool>();
        bool uiAccess = false;
        string? error = null;
        long windowHandle = 0;
        int style = 0, visiblePixels = 0;
        OverlayWindow? overlay = null;
        try
        {
            uiAccess = HostSecurity.GetUiAccess();
            checks["uiAccessGranted"] = uiAccess;
            if (uiAccess)
            {
                IntPtr foreground = NativeMethods.GetForegroundWindow();
                var settings = new CrosshairSettings();
                overlay = new OverlayWindow(settings);
                overlay.SetVisible(true); Pump();
                windowHandle = overlay.Handle.ToInt64();
                style = NativeMethods.GetWindowLong(overlay.Handle, NativeMethods.GwlExStyle);
                checks["layered"] = (style & NativeMethods.Layered) != 0;
                checks["transparent"] = (style & NativeMethods.Transparent) != 0;
                checks["nonActivating"] = (style & NativeMethods.NoActivate) != 0;
                checks["topmost"] = (style & 8) != 0;
                checks["noFocusSteal"] = NativeMethods.GetForegroundWindow() == foreground;
                checks["visible"] = NativeMethods.IsWindowVisible(overlay.Handle);
                NativeMethods.GetWindowRect(overlay.Handle, out var rect);
                var bounds = overlay.SelectedScreen.Bounds;
                checks["centered"] = rect.Left + (rect.Right - rect.Left) / 2 == bounds.Left + bounds.Width / 2 &&
                                     rect.Top + (rect.Bottom - rect.Top) / 2 == bounds.Top + bounds.Height / 2;
                var visual = (FrameworkElement)overlay.Content;
                visual.UpdateLayout();
                var bitmap = new RenderTargetBitmap(400, 400, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                byte[] pixels = new byte[400 * 400 * 4];
                bitmap.CopyPixels(pixels, 1600, 0);
                visiblePixels = Enumerable.Range(0, 400 * 400).Count(i => pixels[i * 4 + 3] != 0);
                checks["crosshairPixels"] = visiblePixels > 20;
                settings.OffsetX = 41; settings.OffsetY = -29; overlay.Position();
                NativeMethods.GetWindowRect(overlay.Handle, out var shifted);
                checks["offsets"] = shifted.Left - rect.Left == 41 && shifted.Top - rect.Top == -29;
                overlay.SetVisible(false);
                checks["hidden"] = !NativeMethods.IsWindowVisible(overlay.Handle);
            }
            else error = "UIAccess 未授予；未创建覆盖窗口。请使用已签名并安装到受保护目录的组件。";
        }
        catch (Exception exception) { error = exception.Message; }
        finally { overlay?.Close(); }
        bool passed = error is null && checks.Count >= 10 && checks.Values.All(x => x);
        var report = new
        {
            capturedAtUtc = DateTimeOffset.UtcNow, processId = Environment.ProcessId,
            uiAccess, elevated = HostSecurity.IsElevated(), executable = Environment.ProcessPath,
            windowHandle, extendedStyle = $"0x{style:X8}", visiblePixels, passed, checks, error,
            limitation = "Desktop diagnostics only. CS fullscreen, display presentation path, latency and anti-cheat compatibility are not validated."
        };
        using var file = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(file, report, new JsonSerializerOptions { WriteIndented = true });
        return passed ? 0 : 2;
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }
}
