using System.Runtime.InteropServices;
using System.Windows;

namespace WorkspaceLauncher;

public partial class App : System.Windows.Application
{

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var mainWindow = new MainWindow();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        foreach (Window window in System.Windows.Application.Current.Windows)
        {
            try { window.Close(); } catch { /* Ignore */ }
        }
        base.OnExit(e);
    }
}
