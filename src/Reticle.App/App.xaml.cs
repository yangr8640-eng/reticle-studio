using System.Windows;

namespace Reticle.App;

public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--diagnostics"))
        {
            // Closing a diagnostic window must not terminate the run or mask its exit code.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            string path = e.Args.SkipWhile(x => x != "--diagnostics").Skip(1).FirstOrDefault() ?? "diagnostics.json";
            int result = Diagnostics.Run(path);
            Shutdown(result);
            return;
        }
        bool independent = AppVersion.IsIndependent || e.Args.Contains("--independent");
        instance = new Mutex(true, independent ? "Local\\ReticleStudio.Independent" : "Local\\ReticleStudio.Desktop", out bool first);
        if (!first)
        {
            MessageBox.Show("准星工具已在运行。请从任务栏或托盘打开设置。", "Reticle Studio");
            Shutdown(); return;
        }
        MainWindow = new MainWindow(preferIndependent: independent);
        MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
}
