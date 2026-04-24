using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using file_sync.ViewModels;

namespace file_sync;

public partial class ObjectStorageSyncWindow : Window
{
    private readonly ObjectStorageSyncViewModel _viewModel;

    public ObjectStorageSyncWindow()
    {
        InitializeComponent();

        _viewModel = new ObjectStorageSyncViewModel();
        DataContext = _viewModel;

        // 设置默认 Endpoint
        UpdateEndpointHint(0);

        // 添加事件处理器
        BrowseButton.Click += BrowseButton_Click;
        ScanButton.Click += ScanButton_Click;
        SyncButton.Click += SyncButton_Click;
        CancelButton.Click += CancelButton_Click;
        BackButton.Click += BackButton_Click;
        StorageTypeComboBox.SelectionChanged += StorageTypeComboBox_SelectionChanged;

        // 同步模式 RadioButton 事件
        Mode0Radio.Checked += SyncModeRadio_Checked;
        Mode1Radio.Checked += SyncModeRadio_Checked;
        Mode2Radio.Checked += SyncModeRadio_Checked;

        // PasswordBox 事件
        AccessKeyBox.PasswordChanged += AccessKeyBox_PasswordChanged;
        SecretKeyBox.PasswordChanged += SecretKeyBox_PasswordChanged;

        // 日志自动滚动
        SubscribeToLogCollection();
    }

    private void AccessKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.AccessKey = AccessKeyBox.Password;
    }

    private void SecretKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.SecretKey = SecretKeyBox.Password;
    }

    private void StorageTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StorageTypeComboBox.SelectedItem is ComboBoxItem item)
        {
            _viewModel.StorageType = StorageTypeComboBox.SelectedIndex;
            UpdateEndpointHint(StorageTypeComboBox.SelectedIndex);
        }
    }

    private void SyncModeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton radio && radio.Tag is string tag && int.TryParse(tag, out int mode))
        {
            _viewModel.SyncMode = mode;
        }
    }

    private void UpdateEndpointHint(int type)
    {
        EndpointHint.Text = type switch
        {
            0 => "s3.amazonaws.com",
            1 => "oss-cn-hangzhou.aliyuncs.com",
            2 => "cos.ap-guangzhou.myqcloud.com",
            3 => "http://localhost:9000",
            _ => ""
        };
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

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择本地目录",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        var helper = new System.Windows.Interop.WindowInteropHelper(this);
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
            _viewModel.LocalDirectory = result;
        }
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.ScanAsync();
    }

    private async void SyncButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.SyncAsync();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Cancel();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        BrowseButton.Click -= BrowseButton_Click;
        ScanButton.Click -= ScanButton_Click;
        SyncButton.Click -= SyncButton_Click;
        CancelButton.Click -= CancelButton_Click;
        BackButton.Click -= BackButton_Click;
        StorageTypeComboBox.SelectionChanged -= StorageTypeComboBox_SelectionChanged;
        Mode0Radio.Checked -= SyncModeRadio_Checked;
        Mode1Radio.Checked -= SyncModeRadio_Checked;
        Mode2Radio.Checked -= SyncModeRadio_Checked;
        AccessKeyBox.PasswordChanged -= AccessKeyBox_PasswordChanged;
        SecretKeyBox.PasswordChanged -= SecretKeyBox_PasswordChanged;
    }

    private class Win32Window : System.Windows.Forms.IWin32Window
    {
        public Win32Window(IntPtr handle) => Handle = handle;
        public IntPtr Handle { get; }
    }
}
