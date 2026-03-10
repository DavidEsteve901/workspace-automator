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
    
    // Agrega esto en el constructor o OnStartup de App.xaml.cs
    [DllImport("shcore.dll")]
    static extern int SetProcessDpiAwareness(int value);

    protected override void OnStartup(StartupEventArgs e)
    {
        // 2 = Process_Per_Monitor_DPI_Aware
        SetProcessDpiAwareness(2); 
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
    }
}
