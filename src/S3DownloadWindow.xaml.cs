using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HandyControl.Controls;
using file_sync.ViewModels;

namespace file_sync;

public partial class S3DownloadWindow : HandyControl.Controls.Window
{
    private readonly S3DownloadViewModel _viewModel;

    public S3DownloadWindow()
    {
        InitializeComponent();

        _viewModel = new S3DownloadViewModel();
        DataContext = _viewModel;

        UpdateEndpointHint(0);

        StorageTypeComboBox.SelectionChanged += StorageTypeComboBox_SelectionChanged;
        AccessKeyBox.PasswordChanged += AccessKeyBox_PasswordChanged;
        SecretKeyBox.PasswordChanged += SecretKeyBox_PasswordChanged;
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
        UpdateEndpointHint(StorageTypeComboBox.SelectedIndex);
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

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择下载目录",
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

    private async void ListButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.ListRemoteFilesAsync();
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.DownloadAsync();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Cancel();
    }

    private class Win32Window : System.Windows.Forms.IWin32Window
    {
        public Win32Window(IntPtr handle) => Handle = handle;
        public IntPtr Handle { get; }
    }
}
