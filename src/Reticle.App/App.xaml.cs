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
            string path = e.Args.SkipWhile(x => x != "--diagnostics").Skip(1).FirstOrDefault() ?? "diagnostics.json";
            int result = Diagnostics.Run(path);
            Shutdown(result);
            return;
        }
        instance = new Mutex(true, "Local\\ReticleStudio.Desktop", out bool first);
        if (!first)
        {
            MessageBox.Show("准星工具已在运行。请从任务栏或托盘打开设置。", "Reticle Studio");
            Shutdown(); return;
        }
        MainWindow = new MainWindow();
        MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
}
