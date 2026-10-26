using System.IO;
using System.Windows;

namespace Reticle.OverlayHost;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            HostOptions options = HostOptions.Parse(args);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            if (options.DiagnosticPath is not null) return HostDiagnostics.Run(options.DiagnosticPath);
            HostSession? session = null;
            app.DispatcherUnhandledException += (_, e) =>
            {
                e.Handled = true; session?.Dispose(); app.Shutdown(3);
            };
            app.Startup += async (_, _) =>
            {
                session = new HostSession(options);
                int result = await session.RunAsync();
                app.Shutdown(result);
            };
            return app.Run();
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(error.Message, "Reticle Studio · 独立全屏组件", MessageBoxButton.OK, MessageBoxImage.Warning);
            return 2;
        }
    }
}
