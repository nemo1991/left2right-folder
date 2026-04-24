using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Globalization;
using System.Windows.Interop;
using file_sync.Services;
using file_sync.ViewModels;

namespace file_sync;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        // 初始化依赖注入
        var fileScanner = new FileScanner();
        var hashCalculator = new HashCalculator();
        var fileComparator = new FileComparator();
        var fileMigrator = new FileMigrator();
        var reportGenerator = new CsvReportGenerator();
        var appState = new AppState();

        // 初始化 AppState
        _ = appState.InitializeAsync();

        _viewModel = new MainViewModel(
            fileScanner,
            hashCalculator,
            fileComparator,
            fileMigrator,
            reportGenerator,
            appState);

        InitializeComponent();
        DataContext = _viewModel;

        // 添加事件处理器
        BrowseSourceButton.Click += BrowseSourceButton_Click;
        BrowseTargetButton.Click += BrowseTargetButton_Click;
        ScanButton.Click += ScanButton_Click;
        MigrateButton.Click += MigrateButton_Click;

        // 添加 BoolToColorConverter 到资源
        Resources.Add("BoolToColorConverter", new BoolToColorConverter());
    }

    private async void BrowseSourceButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowFolderDialogAsync("source");
    }

    private async void BrowseTargetButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowFolderDialogAsync("target");
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.ScanDirectoriesAsync();
    }

    private async void MigrateButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.MigrateAsync();
    }

    private async Task ShowFolderDialogAsync(string type)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = type == "source" ? "选择原目录" : "选择目标目录",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        var helper = new WindowInteropHelper(this);
        var owner = new Win32Window(helper.Handle);

        string? result = null;
        await Task.Run(() =>
        {
            if (dialog.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK)
            {
                result = dialog.SelectedPath;
            }
        });

        if (result != null)
        {
            if (type == "source")
            {
                _viewModel.SourceDirectory = result;
            }
            else
            {
                _viewModel.TargetDirectory = result;
            }
        }
    }

    private class Win32Window : System.Windows.Forms.IWin32Window
    {
        public Win32Window(IntPtr handle) => Handle = handle;
        public IntPtr Handle { get; }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        BrowseSourceButton.Click -= BrowseSourceButton_Click;
        BrowseTargetButton.Click -= BrowseTargetButton_Click;
        ScanButton.Click -= ScanButton_Click;
        MigrateButton.Click -= MigrateButton_Click;
    }
}

public class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isError && isError)
        {
            return new SolidColorBrush(Colors.Red);
        }
        return new SolidColorBrush(Colors.Black);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
