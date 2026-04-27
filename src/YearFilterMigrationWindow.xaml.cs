using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using HandyControl.Controls;
using file_sync.ViewModels;

namespace file_sync;

public partial class YearFilterMigrationWindow : HandyControl.Controls.Window
{
    private readonly YearFilterMigrationViewModel _viewModel;

    public YearFilterMigrationWindow()
    {
        InitializeComponent();

        _viewModel = new YearFilterMigrationViewModel();
        DataContext = _viewModel;

        BrowseSourceButton.Click += BrowseSourceButton_Click;
        BrowseTargetButton.Click += BrowseTargetButton_Click;
        ScanButton.Click += ScanButton_Click;
        MigrateButton.Click += MigrateButton_Click;
        CancelButton.Click += CancelButton_Click;
        BackButton.Click += BackButton_Click;
        YearListBox.SelectionChanged += YearListBox_SelectionChanged;

        SubscribeToLogCollection();
    }

    private void YearDropdownButton_Click(object sender, RoutedEventArgs e)
    {
        if (YearDropdownPopup.IsOpen)
        {
            YearDropdownPopup.IsOpen = false;
            return;
        }

        var currentYear = DateTime.Now.Year;
        var startYear = _viewModel.IsYearValid ? _viewModel.ParsedYear : currentYear;
        var years = new List<YearItem>();
        for (int y = startYear + 10; y >= startYear - 30; y--)
        {
            years.Add(new YearItem { Year = y, YearText = y.ToString() });
        }
        YearListBox.ItemsSource = years;
        YearListBox.SelectedValue = startYear;
        YearDropdownPopup.IsOpen = true;
    }

    private void YearListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (YearListBox.SelectedItem is YearItem item)
        {
            YearTextBox.Text = item.YearText;
            YearDropdownPopup.IsOpen = false;
        }
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
        await _viewModel.ScanAsync();
    }

    private async void MigrateButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.MigrateAsync();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Cancel();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async Task ShowFolderDialogAsync(string type)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = type == "source" ? "选择源目录" : "选择目标目录",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
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
        CancelButton.Click -= CancelButton_Click;
        BackButton.Click -= BackButton_Click;
        YearListBox.SelectionChanged -= YearListBox_SelectionChanged;
    }
}

public class YearItem
{
    public int Year { get; set; }
    public string YearText { get; set; } = "";
}
