using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using file_sync.Services;
using file_sync.ViewModels;

namespace file_sync;

public partial class MainWindow : HandyControl.Controls.Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        var fileScanner = new FileScanner();
        var hashCalculator = new HashCalculator();
        var fileComparator = new FileComparator();
        var fileMigrator = new FileMigrator();
        var reportGenerator = new CsvReportGenerator();


        _viewModel = new MainViewModel(
            fileScanner,
            hashCalculator,
            fileComparator,
            fileMigrator,
            reportGenerator
            );

        InitializeComponent();
        DataContext = _viewModel;

        BrowseSourceButton.Click += BrowseSourceButton_Click;
        BrowseTargetButton.Click += BrowseTargetButton_Click;
        ScanButton.Click += ScanButton_Click;
        MigrateButton.Click += MigrateButton_Click;
        CancelButton.Click += CancelButton_Click;

        SubscribeToLogCollection();
    }

    private void SubscribeToLogCollection()
    {
        if (_viewModel.Logs is ObservableCollection<LogEntry> collection)
        {
            collection.CollectionChanged += (s, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Add && LogListBox.Items.Count > 0)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (LogListBox.Items.Count > 0)
                        {
                            LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
                        }
                    }));
                }
            };
        }
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

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Cancel();
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

        if (_viewModel.Logs is ObservableCollection<LogEntry> collection)
        {
            collection.CollectionChanged -= (s, ev) => { };
        }
    }
}
