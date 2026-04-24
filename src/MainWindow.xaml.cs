using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Globalization;
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

        // 订阅文件夹选择事件
        _viewModel.RequestFolderSelection += OnRequestFolderSelection;

        // 添加 BoolToColorConverter 到资源
        Resources.Add("BoolToColorConverter", new BoolToColorConverter());
    }

    private async void OnRequestFolderSelection(object? sender, string type)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = type == "source" ? "选择原目录" : "选择目标目录",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        // 设置 owner 窗口句柄以确保正确的模态行为
        var helper = new System.Windows.Interop.WindowInteropHelper(this);
        var ownerHandle = helper.Handle;

        try
        {
            var result = await Task.Run(() => dialog.ShowDialog());

            if (result == System.Windows.Forms.DialogResult.OK)
            {
                if (type == "source")
                {
                    _viewModel.SourceDirectory = dialog.SelectedPath;
                }
                else
                {
                    _viewModel.TargetDirectory = dialog.SelectedPath;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"选择目录失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _viewModel.RequestFolderSelection -= OnRequestFolderSelection;
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
