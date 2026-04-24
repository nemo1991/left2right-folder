using System.Windows;

namespace file_sync;

public partial class HomePage : Window
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
}
