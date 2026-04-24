using System.Windows;

namespace file_sync;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = new HomePage();
        mainWindow.Show();
    }
}
