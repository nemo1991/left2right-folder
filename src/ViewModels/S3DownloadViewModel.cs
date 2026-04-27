using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using file_sync.Services;

namespace file_sync.ViewModels;

public partial class S3DownloadViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    private readonly List<DownloadRecord> _filesToDownload = new();
    private readonly List<DownloadRecordEntry> _downloadRecords = new();
    private record DownloadRecord(string ObjectKey, long Size);
    private record DownloadRecordEntry(string LocalPath, string ObjectKey, long Size, string Status, string? ErrorMessage);

    [ObservableProperty] private string _endpoint = "";
    [ObservableProperty] private string _accessKey = "";
    [ObservableProperty] private string _secretKey = "";
    [ObservableProperty] private string _bucket = "";
    [ObservableProperty] private string _downloadPrefix = "";
    [ObservableProperty] private string _wildcardPattern = "";
    [ObservableProperty] private string _localDirectory = "";

    [ObservableProperty] private bool _canDownload = false;
    [ObservableProperty] private bool _canList = false;
    [ObservableProperty] private bool _canCancel = false;
    [ObservableProperty] private string _statusMessage = "就绪";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isProgressIndeterminate;
    [ObservableProperty] private int _toDownloadCount;
    [ObservableProperty] private string _totalSizeText = "";
    [ObservableProperty] private bool _useTrieScan = true;

    [ObservableProperty] private ObservableCollection<LogEntry> _logs = new();

    partial void OnEndpointChanged(string value) => UpdateCanList();
    partial void OnAccessKeyChanged(string value) => UpdateCanList();
    partial void OnSecretKeyChanged(string value) => UpdateCanList();
    partial void OnBucketChanged(string value) => UpdateCanList();
    partial void OnDownloadPrefixChanged(string value) => UpdateCanList();
    partial void OnWildcardPatternChanged(string value) => UpdateCanList();
    partial void OnLocalDirectoryChanged(string value) => UpdateCanList();

    private void UpdateCanList()
    {
        CanList = !string.IsNullOrEmpty(Endpoint) &&
                  !string.IsNullOrEmpty(AccessKey) &&
                  !string.IsNullOrEmpty(SecretKey) &&
                  !string.IsNullOrEmpty(Bucket) &&
                  !string.IsNullOrEmpty(WildcardPattern) &&
                  !string.IsNullOrEmpty(LocalDirectory);
        CanDownload = CanList;
    }

    private IAmazonS3 CreateClient()
    {
        var config = new AmazonS3Config
        {
            ServiceURL = Endpoint,
            ForcePathStyle = true
        };

        return new AmazonS3Client(AccessKey, SecretKey, config);
    }

    public async Task ListRemoteFilesAsync()
    {
        if (!CanList)
        {
            AddLog("错误：请完善存储配置、通配符和本地目录", true);
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            CanList = false;
            CanDownload = false;
            CanCancel = true;
            IsProgressIndeterminate = true;
            StatusMessage = "正在列出远程文件...";
            Logs.Clear();
            _filesToDownload.Clear();

            AddLog($"扫描模式: {(UseTrieScan ? "前缀树扫描" : "平铺扫描")}");
            AddLog($"搜索前缀: {(string.IsNullOrEmpty(DownloadPrefix) ? "(根目录)" : DownloadPrefix)}");
            AddLog($"匹配模式: {WildcardPattern}");

            if (UseTrieScan)
            {
                await ListWithTrieAsync(ct);
            }
            else
            {
                await ListFlatAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消列出";
            CanCancel = false;
            IsProgressIndeterminate = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"列出失败：{ex.Message}";
            AddLog($"错误：{ex.Message}", true);
        }
        finally
        {
            CanList = true;
            CanDownload = _filesToDownload.Count > 0;
            CanCancel = false;
        }
    }

    private async Task ListWithTrieAsync(CancellationToken ct)
    {
        using var client = CreateClient();
        var scanner = new ObjectStorageScanner(client, Bucket, DownloadPrefix, WildcardPattern, ct);
        var progress = new Progress<string>(msg => AddLog(msg));
        await scanner.ScanAsync(progress);

        foreach (var file in scanner.MatchedFiles)
        {
            _filesToDownload.Add(new DownloadRecord(file.Key, file.Size));
        }

        ToDownloadCount = scanner.MatchedFiles.Count;
        StatusMessage = $"找到 {ToDownloadCount} 个匹配的文件";
        IsProgressIndeterminate = false;
        ProgressValue = 100;

        AddLog($"扫描统计: {scanner.Statistics}");
    }

    private async Task ListFlatAsync(CancellationToken ct)
    {
        using var client = CreateClient();
        var searchPrefix = string.IsNullOrEmpty(DownloadPrefix) ? null : DownloadPrefix.TrimEnd('/') + "/";
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(WildcardPattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        var matchingFiles = new List<S3Object>();
        var listRequest = new ListObjectsV2Request
        {
            BucketName = Bucket,
            Prefix = searchPrefix
        };

        ListObjectsV2Response listResponse;
        do
        {
            ct.ThrowIfCancellationRequested();
            listResponse = await client.ListObjectsV2Async(listRequest, ct);

            foreach (var obj in listResponse.S3Objects)
            {
                var key = obj.Key;
                if (!string.IsNullOrEmpty(searchPrefix) && key.StartsWith(searchPrefix))
                {
                    key = key.Substring(searchPrefix.Length);
                }

                if (System.Text.RegularExpressions.Regex.IsMatch(key, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    matchingFiles.Add(obj);
                    _filesToDownload.Add(new DownloadRecord(obj.Key, obj.Size));
                }
            }

            listRequest.ContinuationToken = listResponse.NextContinuationToken;
        } while (listResponse.IsTruncated);

        ToDownloadCount = matchingFiles.Count;
        long totalSize = matchingFiles.Sum(f => f.Size);

        StatusMessage = $"找到 {ToDownloadCount} 个匹配的文件";
        IsProgressIndeterminate = false;
        ProgressValue = 100;

        AddLog($"列出完成 - 共找到 {ToDownloadCount} 个匹配文件，总大小 {FormatSize(totalSize)}");
    }

    public async Task DownloadAsync()
    {
        if (_filesToDownload.Count == 0)
        {
            AddLog("没有可下载的文件，请先点击&quot;列出文件&quot;", true);
            return;
        }

        if (string.IsNullOrEmpty(LocalDirectory))
        {
            AddLog("错误：请选择本地下载目录", true);
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            CanList = false;
            CanDownload = false;
            CanCancel = true;
            IsProgressIndeterminate = false;
            ProgressValue = 0;
            StatusMessage = "正在下载文件...";
            _downloadRecords.Clear();

            using var client = CreateClient();
            int downloadedCount = 0;
            int errorCount = 0;
            int total = _filesToDownload.Count;
            long totalBytes = _filesToDownload.Sum(f => f.Size);
            long downloadedBytes = 0;

            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();

                var file = _filesToDownload[i];
                try
                {
                    var localPath = Path.Combine(LocalDirectory, Path.GetFileName(file.ObjectKey));

                    if (File.Exists(localPath))
                    {
                        _downloadRecords.Add(new DownloadRecordEntry(localPath, file.ObjectKey, file.Size, "跳过", "文件已存在"));
                        AddLog($"跳过（文件已存在）：{Path.GetFileName(file.ObjectKey)}");
                    }
                    else
                    {
                        var getRequest = new GetObjectRequest
                        {
                            BucketName = Bucket,
                            Key = file.ObjectKey
                        };

                        using var response = await client.GetObjectAsync(getRequest, ct);
                        await response.WriteResponseStreamToFileAsync(localPath, false, ct);

                        downloadedCount++;
                        downloadedBytes += file.Size;
                        _downloadRecords.Add(new DownloadRecordEntry(localPath, file.ObjectKey, file.Size, "已下载", null));
                        AddLog($"已下载：{file.ObjectKey}");
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    _downloadRecords.Add(new DownloadRecordEntry("", file.ObjectKey, file.Size, "失败", ex.Message));
                    AddLog($"下载失败：{file.ObjectKey} - {ex.Message}", true);
                }

                ProgressValue = (i + 1) * 100.0 / total;
                StatusMessage = $"正在下载... {i + 1}/{total}";
            }

            StatusMessage = $"下载完成：成功 {downloadedCount}，失败 {errorCount}";

            // 生成报告
            await GenerateReportCsvAsync(downloadedCount, errorCount, total);

            AddLog($"下载完成！成功 {downloadedCount}，失败 {errorCount}");

            CanDownload = true;
            CanList = true;
            CanCancel = false;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消下载";
            CanCancel = false;
            IsProgressIndeterminate = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"下载失败：{ex.Message}";
            AddLog($"错误：{ex.Message}", true);
        }
        finally
        {
            CanCancel = false;
        }
    }

    private async Task GenerateReportCsvAsync(int downloadedCount, int errorCount, int total)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"S3下载报告_{timestamp}.csv",
                DefaultExt = ".csv",
                Filter = "CSV 文件|*.csv|所有文件|*.*"
            };

            if (dialog.ShowDialog() != true)
                return;

            await Task.Run(() =>
            {
                var sb = new StringBuilder();
                sb.AppendLine("操作,本地路径,对象键,文件大小,状态,错误信息");

                foreach (var record in _downloadRecords)
                {
                    sb.AppendLine($"{EscapeCsv(record.Status)},{EscapeCsv(record.LocalPath)},{EscapeCsv(record.ObjectKey)},{FormatSize(record.Size)},{EscapeCsv(record.ErrorMessage ?? "")}");
                }

                sb.AppendLine();
                sb.AppendLine($"摘要：总计 {total}，成功 {downloadedCount}，失败 {errorCount}");

                File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
            });

            AddLog($"报告已保存：{dialog.FileName}");
        }
        catch (Exception ex)
        {
            AddLog($"保存报告失败：{ex.Message}", true);
        }
    }

    public void Cancel()
    {
        _cts?.Cancel();
        StatusMessage = "正在取消...";
    }

    private void AddLog(string message, bool isError = false)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            Logs.Add(new LogEntry(message, isError));
        });
    }

    private static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
