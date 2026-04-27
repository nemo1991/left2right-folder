using System.Windows;
using HandyControl.Controls;

namespace file_sync;

public partial class HomePage : HandyControl.Controls.Window
{
    public HomePage()
    {
        InitializeComponent();
    }

    private void FileMigrationButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        var migrationWindow = new MainWindow
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        migrationWindow.Closed += (s, args) => Show();
        migrationWindow.Show();
    }

    private void YearFilterButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        var yearFilterWindow = new YearFilterMigrationWindow
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        yearFilterWindow.Closed += (s, args) => Show();
        yearFilterWindow.Show();
    }

    private void ObjectStorageSyncButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        var syncWindow = new ObjectStorageSyncWindow
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        syncWindow.Closed += (s, args) => Show();
        syncWindow.Show();
    }

    private void S3DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        var downloadWindow = new S3DownloadWindow
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        downloadWindow.Closed += (s, args) => Show();
        downloadWindow.Show();
    }
}
