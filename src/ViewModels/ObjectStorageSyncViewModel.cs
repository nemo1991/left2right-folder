using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using file_sync.Services;

namespace file_sync.ViewModels;

public partial class ObjectStorageSyncViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    private List<SyncRecord> _filesToSync = new();
    private readonly List<SyncRecord> _syncRecords = new();

    private record SyncRecord(string LocalPath, string ObjectKey, long Size, string Status, string? ErrorMessage);
    private record RemoteFileInfo(long Size, DateTime LastModified);

    [ObservableProperty] private string _localDirectory = "";
    [ObservableProperty] private string _endpoint = "";
    [ObservableProperty] private string _accessKey = "";
    [ObservableProperty] private string _secretKey = "";
    [ObservableProperty] private string _bucket = "";
    [ObservableProperty] private string _prefix = "";
    [ObservableProperty] private int _storageType = 0; // 0=S3, 1=OSS, 2=COS, 3=MinIO
    [ObservableProperty] private int _syncMode = 1; // 0=仅上传新增, 1=上传+更新, 2=上传后删除
    [ObservableProperty] private string _statusMessage = "就绪";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isProgressIndeterminate;
    [ObservableProperty] private bool _canScan = false;
    [ObservableProperty] private bool _canSync;
    [ObservableProperty] private bool _canCancel;
    [ObservableProperty] private string _scanButtonContent = "扫描";
    [ObservableProperty] private string _syncButtonContent = "开始同步";
    [ObservableProperty] private int _toSyncCount;
    [ObservableProperty] private string _totalSizeText = "";
    [ObservableProperty] private Visibility _totalSizeVisibility = Visibility.Collapsed;
    [ObservableProperty] private ObservableCollection<LogEntry> _logs = new();

    // 下载相关
    [ObservableProperty] private string _downloadPrefix = "";
    [ObservableProperty] private string _wildcardPattern = "";
    [ObservableProperty] private bool _canDownload = false;
    [ObservableProperty] private int _toDownloadCount;
    [ObservableProperty] private bool _useTrieScan = true; // 默认使用前缀树扫描
    private readonly List<DownloadRecord> _filesToDownload = new();
    private readonly List<SyncRecord> _downloadRecords = new();
    private record DownloadRecord(string ObjectKey, long Size);

    partial void OnLocalDirectoryChanged(string value)
    {
        ResetState();
        UpdateCanScan();
    }

    partial void OnEndpointChanged(string value) => UpdateCanScan();
    partial void OnAccessKeyChanged(string value) => UpdateCanScan();
    partial void OnSecretKeyChanged(string value) => UpdateCanScan();
    partial void OnBucketChanged(string value) => UpdateCanScan();
    partial void OnDownloadPrefixChanged(string value) => UpdateCanDownload();
    partial void OnWildcardPatternChanged(string value) => UpdateCanDownload();

    private void UpdateCanScan()
    {
        CanScan = !string.IsNullOrEmpty(LocalDirectory) &&
                  !string.IsNullOrEmpty(Endpoint) &&
                  !string.IsNullOrEmpty(AccessKey) &&
                  !string.IsNullOrEmpty(SecretKey) &&
                  !string.IsNullOrEmpty(Bucket);
    }

    private void UpdateCanDownload()
    {
        CanDownload = !string.IsNullOrEmpty(LocalDirectory) &&
                     !string.IsNullOrEmpty(Endpoint) &&
                     !string.IsNullOrEmpty(AccessKey) &&
                     !string.IsNullOrEmpty(SecretKey) &&
                     !string.IsNullOrEmpty(Bucket) &&
                     !string.IsNullOrEmpty(WildcardPattern);
    }

    private void ResetState()
    {
        ToSyncCount = 0;
        TotalSizeText = "";
        TotalSizeVisibility = Visibility.Collapsed;
        _filesToSync.Clear();
    }

    private IAmazonS3 CreateClient()
    {
        var config = new AmazonS3Config
        {
            ServiceURL = Endpoint,
            ForcePathStyle = true
        };

        switch (StorageType)
        {
            case 1: // 阿里云 OSS
                config.ServiceURL = Endpoint;
                break;
            case 2: // 腾讯云 COS
                config.ServiceURL = Endpoint;
                break;
            case 3: // MinIO
                config.ServiceURL = Endpoint;
                config.UseHttp = true;
                break;
        }

        return new AmazonS3Client(AccessKey, SecretKey, config);
    }

    public async Task ScanAsync()
    {
        if (!CanScan)
        {
            AddLog("错误：请完善存储配置", true);
            return;
        }

        if (!Directory.Exists(LocalDirectory))
        {
            AddLog($"错误：本地目录不存在：{LocalDirectory}", true);
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            CanScan = false;
            CanSync = false;
            CanCancel = true;
            IsProgressIndeterminate = true;
            StatusMessage = "正在扫描文件...";
            ScanButtonContent = "扫描中...";
            Logs.Clear();
            _filesToSync.Clear();
            ResetState();

            // 获取远程文件列表
            var remoteFiles = new Dictionary<string, RemoteFileInfo>(StringComparer.OrdinalIgnoreCase);
            AddLog("正在获取远程文件列表...");

            using var client = CreateClient();
            var listRequest = new ListObjectsV2Request
            {
                BucketName = Bucket,
                Prefix = string.IsNullOrEmpty(Prefix) ? null : Prefix.Trim('/') + "/"
            };

            ListObjectsV2Response listResponse;
            do
            {
                ct.ThrowIfCancellationRequested();
                listResponse = await client.ListObjectsV2Async(listRequest, ct);

                foreach (var obj in listResponse.S3Objects)
                {
                    if (!remoteFiles.ContainsKey(obj.Key))
                    {
                        remoteFiles[obj.Key] = new RemoteFileInfo(obj.Size, obj.LastModified);
                    }
                }

                listRequest.ContinuationToken = listResponse.NextContinuationToken;
            } while (listResponse.IsTruncated);

            AddLog($"远程文件列表获取完成，共 {remoteFiles.Count} 个文件");

            // 扫描本地文件
            var allFiles = Directory.EnumerateFiles(LocalDirectory, "*.*", SearchOption.AllDirectories);
            int totalCount = 0;

            foreach (var filePath in allFiles)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var info = new FileInfo(filePath);
                    if ((info.Attributes & FileAttributes.System) != 0)
                        continue;

                    var objectKey = BuildObjectKey(filePath);
                    bool needSync = false;

                    if (!remoteFiles.ContainsKey(objectKey))
                    {
                        // 文件不存在于远程，需要上传
                        needSync = true;
                    }
                    else
                    {
                        // 文件已存在，根据模式判断
                        var remote = remoteFiles[objectKey];
                        if (SyncMode == 1)
                        {
                            // 上传+更新：如果本地文件更新则同步
                            if (info.LastWriteTimeUtc > remote.LastModified || info.Length != remote.Size)
                            {
                                needSync = true;
                            }
                        }
                    }

                    if (needSync)
                    {
                        _filesToSync.Add(new SyncRecord(filePath, objectKey, info.Length, "待同步", null));
                    }

                    totalCount++;
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }

            long totalSize = _filesToSync.Sum(f => f.Size);
            ToSyncCount = _filesToSync.Count;
            TotalSizeText = FormatSize(totalSize);
            TotalSizeVisibility = Visibility.Visible;

            StatusMessage = $"扫描完成 - 共扫描 {totalCount} 个文件，找到 {ToSyncCount} 个需要同步的文件";
            ScanButtonContent = "重新扫描";
            CanScan = true;
            CanSync = ToSyncCount > 0;
            CanCancel = false;
            IsProgressIndeterminate = false;
            ProgressValue = 100;

            AddLog($"扫描完成 - 共扫描 {totalCount} 个文件，找到 {ToSyncCount} 个需要同步的文件");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消扫描";
            ScanButtonContent = "扫描";
            CanScan = true;
            CanCancel = false;
            IsProgressIndeterminate = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"扫描失败：{ex.Message}";
            ScanButtonContent = "扫描";
            CanScan = true;
            CanCancel = false;
            IsProgressIndeterminate = false;
            AddLog($"错误：{ex.Message}", true);
        }
    }

    public async Task ListRemoteFilesAsync()
    {
        if (!CanDownload)
        {
            AddLog("错误：请完善存储配置和通配符", true);
            return;
        }

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            CanScan = false;
            CanSync = false;
            CanDownload = false;
            CanCancel = true;
            IsProgressIndeterminate = true;
            StatusMessage = "正在列出远程文件...";
            Logs.Clear();
            _filesToDownload.Clear();

            AddLog($"扫描模式: {(UseTrieScan ? "前缀树扫描（推荐）" : "平铺扫描")}");
            AddLog($"搜索前缀: {(string.IsNullOrEmpty(DownloadPrefix) ? "(根目录)" : DownloadPrefix)}");
            AddLog($"匹配模式: {WildcardPattern}");

            if (UseTrieScan)
            {
                await ListRemoteFilesWithTrieAsync(ct);
            }
            else
            {
                await ListRemoteFilesFlatAsync(ct);
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
            CanDownload = _filesToDownload.Count > 0;
            CanScan = true;
            CanCancel = false;
        }
    }

    /// <summary>
    /// 使用前缀树扫描列出远程文件（推荐）
    /// 通过逐层展开目录树，只扫描可能包含匹配文件的路径
    /// </summary>
    private async Task ListRemoteFilesWithTrieAsync(CancellationToken ct)
    {
        using var client = CreateClient();
        var scanner = new ObjectStorageScanner(client, Bucket, DownloadPrefix, WildcardPattern, ct);

        var progress = new Progress<string>(msg => AddLog(msg));
        await scanner.ScanAsync(progress);

        // 将扫描结果添加到下载列表
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

    /// <summary>
    /// 使用平铺扫描列出远程文件（传统方式）
    /// 一次性列出所有文件再过滤
    /// </summary>
    private async Task ListRemoteFilesFlatAsync(CancellationToken ct)
    {
        using var client = CreateClient();

        var searchPrefix = string.IsNullOrEmpty(DownloadPrefix) ? null : DownloadPrefix.TrimEnd('/') + "/";

        // 将通配符转换为正则表达式
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(WildcardPattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        var matchingFiles = new List<S3Object>();

        // 分页获取文件列表
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

        if (!Directory.Exists(LocalDirectory))
        {
            AddLog($"错误：本地目录不存在：{LocalDirectory}", true);
            return;
        }

        var result = MessageBox.Show(
            $"确认下载 {_filesToDownload.Count} 个文件到本地目录 {LocalDirectory}？",
            "确认下载",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.OK)
            return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            CanScan = false;
            CanSync = false;
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

            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();

                var file = _filesToDownload[i];
                try
                {
                    var localPath = Path.Combine(LocalDirectory, Path.GetFileName(file.ObjectKey));

                    // 如果本地已存在，跳过
                    if (File.Exists(localPath))
                    {
                        _downloadRecords.Add(new SyncRecord(localPath, file.ObjectKey, file.Size, "跳过", "文件已存在"));
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
                        _downloadRecords.Add(new SyncRecord(localPath, file.ObjectKey, file.Size, "已下载", null));
                        AddLog($"已下载：{file.ObjectKey}");
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    _downloadRecords.Add(new SyncRecord("", file.ObjectKey, file.Size, "错误", ex.Message));
                    AddLog($"错误：{file.ObjectKey} - {ex.Message}", true);
                }

                var completed = downloadedCount + errorCount;
                if (completed % 10 == 0 || completed == total)
                {
                    ProgressValue = (double)completed / total * 100;
                    StatusMessage = $"已处理 {completed}/{total}";
                }
            }

            StatusMessage = $"下载完成 - 已下载：{downloadedCount}，跳过：{total - downloadedCount - errorCount}，错误：{errorCount}";
            CanScan = true;
            CanDownload = false;
            CanCancel = false;
            ProgressValue = 100;

            AddLog($"下载完成！已下载：{downloadedCount}，错误：{errorCount}");

            // 生成下载报告
            var reportPath = GenerateDownloadReportCsv(downloadedCount, errorCount, total);
            if (!string.IsNullOrEmpty(reportPath))
            {
                AddLog($"下载报告已保存：{reportPath}");
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消下载";
            CanDownload = true;
            CanScan = true;
            CanCancel = false;
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

    private string? GenerateDownloadReportCsv(int downloadedCount, int errorCount, int total)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var defaultFileName = $"下载报告_{timestamp}.csv";

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = defaultFileName,
                DefaultExt = ".csv",
                Filter = "CSV 文件|*.csv|所有文件|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dialog.ShowDialog() != true)
                return null;

            var sb = new StringBuilder();
            sb.AppendLine("操作,本地路径,对象键,文件大小,状态,错误信息");

            foreach (var record in _downloadRecords)
            {
                sb.AppendLine($"{EscapeCsv(record.Status)},{EscapeCsv(record.LocalPath)},{EscapeCsv(record.ObjectKey)},{FormatSize(record.Size)},{EscapeCsv(record.ErrorMessage ?? "")}");
            }

            sb.AppendLine();
            sb.AppendLine("===== 下载摘要 =====");
            sb.AppendLine($"存储桶：{Bucket}");
            sb.AppendLine($"搜索前缀：{(string.IsNullOrEmpty(DownloadPrefix) ? "(无)" : DownloadPrefix)}");
            sb.AppendLine($"匹配模式：{WildcardPattern}");
            sb.AppendLine($"下载时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"总文件数：{total}");
            sb.AppendLine($"已下载：{downloadedCount}");
            sb.AppendLine($"错误：{errorCount}");

            File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
            return dialog.FileName;
        }
        catch (Exception ex)
        {
            AddLog($"报告生成失败：{ex.Message}", true);
            return null;
        }
    }

    public async Task SyncAsync()
    {
        if (_filesToSync.Count == 0) return;

        var result = MessageBox.Show(
            $"确认将 {_filesToSync.Count} 个文件同步到对象存储的 {Bucket} 存储桶？",
            "确认同步",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.OK)
            return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            CanSync = false;
            CanScan = false;
            CanCancel = true;
            IsProgressIndeterminate = false;
            ProgressValue = 0;
            StatusMessage = "正在同步文件...";
            SyncButtonContent = "同步中...";
            _syncRecords.Clear();

            using var client = CreateClient();
            int uploadedCount = 0;
            int deletedCount = 0;
            int errorCount = 0;
            int total = _filesToSync.Count;

            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();

                var file = _filesToSync[i];
                try
                {
                    if (!await Task.Run(() => File.Exists(file.LocalPath)))
                    {
                        _syncRecords.Add(new SyncRecord(file.LocalPath, file.ObjectKey, 0, "跳过", "文件不存在"));
                        errorCount++;
                        AddLog($"跳过（文件不存在）：{Path.GetFileName(file.LocalPath)}");
                        continue;
                    }

                    // 上传文件
                    var putRequest = new PutObjectRequest
                    {
                        BucketName = Bucket,
                        Key = file.ObjectKey,
                        FilePath = file.LocalPath
                    };

                    await client.PutObjectAsync(putRequest, ct);
                    uploadedCount++;
                    _syncRecords.Add(new SyncRecord(file.LocalPath, file.ObjectKey, file.Size, "已上传", null));
                    AddLog($"已上传：{file.ObjectKey}");

                    // 如果选择"上传后删除"模式
                    if (SyncMode == 2)
                    {
                        await Task.Run(() => File.Delete(file.LocalPath));
                        deletedCount++;
                        AddLog($"已删除本地文件：{Path.GetFileName(file.LocalPath)}");
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    _syncRecords.Add(new SyncRecord(file.LocalPath, file.ObjectKey, file.Size, "错误", ex.Message));
                    AddLog($"错误：{Path.GetFileName(file.LocalPath)} - {ex.Message}", true);
                }

                var completed = uploadedCount + errorCount;
                if (completed % 50 == 0 || completed == total)
                {
                    ProgressValue = (double)completed / total * 100;
                    StatusMessage = $"已处理 {completed}/{total}";
                }
            }

            StatusMessage = $"同步完成 - 已上传：{uploadedCount}，已删除：{deletedCount}，错误：{errorCount}";
            SyncButtonContent = "同步完成";
            CanScan = true;
            CanSync = false;
            CanCancel = false;
            ProgressValue = 100;

            AddLog($"同步完成！已上传：{uploadedCount}，已删除：{deletedCount}，错误：{errorCount}");

            // 生成同步报告
            var reportPath = await GenerateReportCsv(uploadedCount, deletedCount, errorCount, total);
            if (!string.IsNullOrEmpty(reportPath))
            {
                AddLog($"同步报告已保存：{reportPath}");
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消同步";
            SyncButtonContent = "开始同步";
            CanSync = true;
            CanScan = true;
            CanCancel = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"同步失败：{ex.Message}";
            SyncButtonContent = "开始同步";
            CanSync = true;
            CanScan = true;
            CanCancel = false;
            AddLog($"错误：{ex.Message}", true);
        }
    }

    public void Cancel()
    {
        _cts?.Cancel();
        StatusMessage = "正在取消...";
    }

    private string BuildObjectKey(string localPath)
    {
        var relativePath = Path.GetRelativePath(LocalDirectory, localPath);
        // 统一使用正斜杠
        relativePath = relativePath.Replace('\\', '/');

        if (string.IsNullOrEmpty(Prefix))
            return relativePath;

        var prefix = Prefix.Trim('/');
        return prefix + "/" + relativePath;
    }

    private static string FormatSize(long bytes)
    {
        double size = bytes;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }
        return unitIndex == 0 ? $"{size:F0} {units[unitIndex]}" : $"{size:F2} {units[unitIndex]}";
    }

    private async Task<string?> GenerateReportCsv(int uploadedCount, int deletedCount, int errorCount, int total)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var defaultFileName = $"同步报告_{timestamp}.csv";

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = defaultFileName,
                DefaultExt = ".csv",
                Filter = "CSV 文件|*.csv|所有文件|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dialog.ShowDialog() != true)
                return null;

            var sb = new StringBuilder();
            sb.AppendLine("操作,源路径,对象键,文件大小,状态,错误信息");

            foreach (var record in _syncRecords)
            {
                sb.AppendLine($"{EscapeCsv(record.Status)},{EscapeCsv(record.LocalPath)},{EscapeCsv(record.ObjectKey)},{FormatSize(record.Size)},{EscapeCsv(record.ErrorMessage ?? "")}");
            }

            sb.AppendLine();
            sb.AppendLine("===== 同步摘要 =====");
            sb.AppendLine($"存储桶：{Bucket}");
            sb.AppendLine($"路径前缀：{(string.IsNullOrEmpty(Prefix) ? "(无)" : Prefix)}");
            sb.AppendLine($"端点：{Endpoint}");
            sb.AppendLine($"本地目录：{LocalDirectory}");
            sb.AppendLine($"同步时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"总文件数：{total}");
            sb.AppendLine($"已上传：{uploadedCount}");
            sb.AppendLine($"已删除：{deletedCount}");
            sb.AppendLine($"错误：{errorCount}");

            await Task.Run(() => File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true)));
            return dialog.FileName;
        }
        catch (Exception ex)
        {
            AddLog($"报告生成失败：{ex.Message}", true);
            return null;
        }
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    private void AddLog(string message, bool isError = false)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var log = new LogEntry($"[{timestamp}] {(isError ? "X " : "")}{message}", isError);

        Application.Current?.Dispatcher.Invoke(() =>
        {
            Logs.Add(log);
            while (Logs.Count > 1000)
                Logs.RemoveAt(0);
        });
    }
}
