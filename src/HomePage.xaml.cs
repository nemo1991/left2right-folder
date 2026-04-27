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
        var migrationWindow = new MainWindow
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        migrationWindow.ShowDialog();
    }

    private void YearFilterButton_Click(object sender, RoutedEventArgs e)
    {
        var yearFilterWindow = new YearFilterMigrationWindow
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        yearFilterWindow.ShowDialog();
    }

    private void ObjectStorageSyncButton_Click(object sender, RoutedEventArgs e)
    {
        var syncWindow = new ObjectStorageSyncWindow
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        syncWindow.ShowDialog();
    }

    private void S3DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        var downloadWindow = new S3DownloadWindow
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        downloadWindow.ShowDialog();
    }
}
