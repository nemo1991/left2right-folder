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
using CommunityToolkit.Mvvm.ComponentModel;

namespace file_sync.ViewModels;

public partial class YearFilterMigrationViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    private List<FileInfo> _filesToMove = new();
    private int _parsedYear;
    private readonly List<MigrationRecord> _migrationRecords = new();

    private record MigrationRecord(string SourcePath, string TargetPath, long Size, string Hash, string Status, string? ErrorMessage);

    [ObservableProperty] private string _sourceDirectory = "";
    [ObservableProperty] private string _targetDirectory = "";
    [ObservableProperty] private string _yearText = DateTime.Now.Year.ToString();
    [ObservableProperty] private string _statusMessage = "就绪";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isProgressIndeterminate;
    [ObservableProperty] private bool _canScan = false;
    [ObservableProperty] private bool _canMigrate;
    [ObservableProperty] private bool _canCancel;
    [ObservableProperty] private string _scanButtonContent = "扫描";
    [ObservableProperty] private string _migrateButtonContent = "开始迁移";
    [ObservableProperty] private int _toMoveCount;
    [ObservableProperty] private string _totalSizeText = "";
    [ObservableProperty] private Visibility _totalSizeVisibility = Visibility.Collapsed;
    [ObservableProperty] private string _targetSubDirInfo = "";
    [ObservableProperty] private string _warningText = "";
    [ObservableProperty] private Visibility _warningVisibility;
    [ObservableProperty] private string _yearInfoText = "";
    [ObservableProperty] private Brush _yearValidationColor = Brushes.Gray;
    [ObservableProperty] private ObservableCollection<LogEntry> _logs = new();

    public bool IsYearValid => _parsedYear > 0;
    public int ParsedYear => _parsedYear;

    public YearFilterMigrationViewModel()
    {
        // 初始化年份显示
        ValidateYear(_yearText);
    }

    private void ValidateYear(string value)
    {
        if (int.TryParse(value, out int year) && year >= 1970 && year <= 2100)
        {
            _parsedYear = year;
            YearInfoText = $"{_parsedYear} 年";
            YearValidationColor = Brushes.Gray;
        }
        else if (!string.IsNullOrWhiteSpace(value))
        {
            _parsedYear = 0;
            YearInfoText = "请输入有效年份（1970-2100）";
            YearValidationColor = Brushes.Red;
        }
        else
        {
            _parsedYear = 0;
            YearInfoText = "";
            YearValidationColor = Brushes.Gray;
        }
        UpdateCanScan();
    }

    partial void OnYearTextChanged(string value)
    {
        ValidateYear(value);
    }

    partial void OnSourceDirectoryChanged(string value)
    {
        ResetState();
        UpdateCanScan();
    }

    partial void OnTargetDirectoryChanged(string value)
    {
        ResetState();
        UpdateCanScan();
    }

    private void UpdateCanScan()
    {
        CanScan = !string.IsNullOrEmpty(SourceDirectory) &&
                  !string.IsNullOrEmpty(TargetDirectory) &&
                  IsYearValid;
    }

    private void ResetState()
    {
        ToMoveCount = 0;
        TotalSizeText = "";
        TotalSizeVisibility = Visibility.Collapsed;
        TargetSubDirInfo = "";
        WarningText = "";
        WarningVisibility = Visibility.Collapsed;
        _filesToMove.Clear();
    }

    public void CheckTargetSubDirectory()
    {
        if (!IsYearValid || string.IsNullOrEmpty(TargetDirectory))
            return;

        var yearSubDir = Path.Combine(TargetDirectory, _parsedYear.ToString());
        if (Directory.Exists(yearSubDir))
        {
            var files = Directory.EnumerateFiles(yearSubDir, "*.*", SearchOption.AllDirectories).ToList();
            if (files.Count > 0)
            {
                WarningText = $"目标目录已存在 {_parsedYear} 文件夹，包含 {files.Count} 个文件";
                WarningVisibility = Visibility.Visible;
            }
            else
            {
                WarningText = $"目标目录已存在 {_parsedYear} 文件夹（空文件夹）";
                WarningVisibility = Visibility.Visible;
            }
        }
        else
        {
            WarningText = "";
            WarningVisibility = Visibility.Collapsed;
        }
    }

    public async Task ScanAsync()
    {
        if (string.IsNullOrEmpty(SourceDirectory) || string.IsNullOrEmpty(TargetDirectory) || !IsYearValid)
        {
            AddLog("错误：请先填写源目录、目标目录和年份", true);
            return;
        }

        if (!Directory.Exists(SourceDirectory))
        {
            AddLog($"错误：源目录不存在：{SourceDirectory}", true);
            return;
        }

        if (!Directory.Exists(TargetDirectory))
        {
            AddLog($"错误：目标目录不存在：{TargetDirectory}", true);
            return;
        }

        // 预检查：目标年份文件夹是否已存在且不为空
        var yearSubDir = Path.Combine(TargetDirectory, _parsedYear.ToString());
        if (Directory.Exists(yearSubDir))
        {
            var existingFiles = Directory.EnumerateFiles(yearSubDir, "*.*", SearchOption.AllDirectories).ToList();
            if (existingFiles.Count > 0)
            {
                AddLog($"错误：目标目录已存在 {_parsedYear} 文件夹且包含 {existingFiles.Count} 个文件，请选择其他目标目录或清空该文件夹", true);
                return;
            }
        }

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            CanScan = false;
            CanMigrate = false;
            CanCancel = true;
            IsProgressIndeterminate = true;
            StatusMessage = "正在扫描文件...";
            ScanButtonContent = "扫描中...";
            Logs.Clear();
            _filesToMove.Clear();
            ResetState();

            var progress = new Progress<(string? fileName, int count)>(p =>
            {
                if (p.fileName != null)
                    StatusMessage = $"正在扫描：{p.fileName}";
            });

            // 在后台线程执行扫描
            var (files, totalCount, totalSize, accessDeniedDirs) = await Task.Run(() =>
            {
                var filesToMove = new List<FileInfo>();
                var accessDenied = new List<string>();
                var allFiles = Directory.EnumerateFiles(SourceDirectory, "*.*", SearchOption.AllDirectories);
                int count = 0;
                long size = 0;
                int fileNameUpdateCounter = 0;

                foreach (var filePath in allFiles)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var info = new FileInfo(filePath);
                        if ((info.Attributes & FileAttributes.System) != 0)
                            continue;

                        if (info.LastWriteTime.Year == _parsedYear)
                        {
                            filesToMove.Add(info);
                            size += info.Length;
                        }

                        count++;
                        fileNameUpdateCounter++;
                        if (fileNameUpdateCounter % 100 == 0)
                        {
                            ((IProgress<(string?, int)>)progress).Report((Path.GetFileName(filePath), count));
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        var dir = Path.GetDirectoryName(filePath);
                        if (!string.IsNullOrEmpty(dir) && !accessDenied.Contains(dir))
                            accessDenied.Add(dir);
                    }
                    catch (IOException) { }
                }

                return (filesToMove, count, size, accessDenied);
            }, ct);

            // 提示无权限的目录
            if (accessDeniedDirs.Count > 0)
            {
                AddLog($"警告：无法访问 {accessDeniedDirs.Count} 个目录或文件，部分内容可能未扫描", true);
            }

            _filesToMove = files;
            ToMoveCount = _filesToMove.Count;
            TotalSizeText = FormatSize(totalSize);
            TotalSizeVisibility = Visibility.Visible;
            TargetSubDirInfo = $"{_parsedYear}";

            CheckTargetSubDirectory();

            StatusMessage = $"扫描完成 - 共扫描 {totalCount} 个文件，找到 {ToMoveCount} 个 {_parsedYear} 年修改的文件";
            ScanButtonContent = "重新扫描";
            CanScan = true;
            CanMigrate = ToMoveCount > 0;
            CanCancel = false;
            IsProgressIndeterminate = false;
            ProgressValue = 100;

            AddLog($"扫描完成 - 共扫描 {totalCount} 个文件，找到 {ToMoveCount} 个 {_parsedYear} 年修改的文件");
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

    public async Task MigrateAsync()
    {
        if (_filesToMove.Count == 0) return;

        var result = MessageBox.Show(
            $"确认将 {_filesToMove.Count} 个文件迁移到目标目录的 {_parsedYear} 子目录中？\n\n{(WarningVisibility == Visibility.Visible ? WarningText + "\n\n" : "")}迁移操作会将文件从源目录移动到目标目录。",
            "确认迁移",
            MessageBoxButton.OKCancel,
            WarningVisibility == Visibility.Visible ? MessageBoxImage.Warning : MessageBoxImage.Question);

        if (result != MessageBoxResult.OK)
            return;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            CanMigrate = false;
            CanScan = false;
            CanCancel = true;
            IsProgressIndeterminate = false;
            ProgressValue = 0;
            StatusMessage = "正在迁移文件...";
            MigrateButtonContent = "迁移中...";

            var yearSubDir = Path.Combine(TargetDirectory, _parsedYear.ToString());
            if (!Directory.Exists(yearSubDir))
            {
                Directory.CreateDirectory(yearSubDir);
                AddLog($"创建目录：{yearSubDir}");
            }

            int total = _filesToMove.Count;
            _migrationRecords.Clear();

            var progress = new Progress<(int index, string message, bool isError, MigrationRecord? record)>(p =>
            {
                if (p.record != null)
                    _migrationRecords.Add(p.record);
                ProgressValue = (double)(p.index + 1) / total * 100;
                StatusMessage = $"已处理 {p.index + 1}/{total}";
                if (!string.IsNullOrEmpty(p.message))
                    AddLog(p.message, p.isError);
            });

            // 在后台线程执行迁移
            await Task.Run(() =>
            {
                for (int i = 0; i < total; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var file = _filesToMove[i];
                    var targetPath = Path.Combine(yearSubDir, file.Name);
                    MigrationRecord? record = null;
                    string? message = null;
                    bool isError = false;

                    try
                    {
                        if (!File.Exists(file.FullName))
                        {
                            record = new MigrationRecord(file.FullName, "", file.Length, "", "跳过", "文件不存在");
                            message = $"跳过（文件不存在）：{file.Name}";
                        }
                        else
                        {
                            // 计算文件hash
                            var hash = ComputeFileHash(file.FullName);

                            if (File.Exists(targetPath))
                            {
                                var baseName = Path.GetFileNameWithoutExtension(file.Name);
                                var extension = file.Extension;
                                var counter = 1;
                                do
                                {
                                    targetPath = Path.Combine(yearSubDir, $"{baseName}_{counter}{extension}");
                                    counter++;
                                } while (File.Exists(targetPath));
                            }

                            var relativePath = Path.GetRelativePath(SourceDirectory, file.FullName);
                            var relativeDir = Path.GetDirectoryName(relativePath);
                            if (!string.IsNullOrEmpty(relativeDir))
                            {
                                var targetSubDir = Path.Combine(yearSubDir, relativeDir);
                                if (!Directory.Exists(targetSubDir))
                                    Directory.CreateDirectory(targetSubDir);
                                targetPath = Path.Combine(yearSubDir, relativePath);
                                var finalTargetDir = Path.GetDirectoryName(targetPath)!;
                                if (!Directory.Exists(finalTargetDir))
                                    Directory.CreateDirectory(finalTargetDir);
                            }

                            File.Move(file.FullName, targetPath);
                            record = new MigrationRecord(file.FullName, targetPath, file.Length, hash, "已移动", null);
                            message = $"已移动：{file.Name}";
                        }
                    }
                    catch (Exception ex)
                    {
                        isError = true;
                        record = new MigrationRecord(file.FullName, "", file.Length, "", "错误", ex.Message);
                        message = $"错误：{file.Name} - {ex.Message}";
                    }

                    ((IProgress<(int, string, bool, MigrationRecord?)>)progress).Report((i, message!, isError, record));
                }
            }, ct);

            int finalMoved = _migrationRecords.Count(r => r.Status == "已移动");
            int finalSkipped = _migrationRecords.Count(r => r.Status == "跳过");
            int finalError = _migrationRecords.Count(r => r.Status == "错误");

            StatusMessage = $"迁移完成 - 已移动：{finalMoved}，跳过：{finalSkipped}，错误：{finalError}";
            MigrateButtonContent = "迁移完成";
            CanScan = true;
            CanMigrate = false;
            CanCancel = false;
            ProgressValue = 100;

            AddLog($"迁移完成！已移动：{finalMoved}，跳过：{finalSkipped}，错误：{finalError}");

            // 生成迁移报告
            var reportPath = GenerateReportCsv(finalMoved, finalSkipped, finalError, total);
            if (!string.IsNullOrEmpty(reportPath))
            {
                AddLog($"迁移报告已保存：{reportPath}");
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消迁移";
            MigrateButtonContent = "开始迁移";
            CanMigrate = true;
            CanScan = true;
            CanCancel = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"迁移失败：{ex.Message}";
            MigrateButtonContent = "开始迁移";
            CanMigrate = true;
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

    private static string ComputeFileHash(string filePath)
    {
        try
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            using var stream = File.OpenRead(filePath);
            var hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }

    private string GenerateReportCsv(int movedCount, int skippedCount, int errorCount, int total)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"年份迁移报告_{_parsedYear}_{timestamp}.csv";
            var filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);

            var sb = new StringBuilder();
            sb.AppendLine("操作,源路径,目标路径,文件大小,文件Hash,错误信息");

            foreach (var record in _migrationRecords)
            {
                sb.AppendLine($"{EscapeCsv(record.Status)},{EscapeCsv(record.SourcePath)},{EscapeCsv(record.TargetPath)},{FormatSize(record.Size)},{record.Hash},{EscapeCsv(record.ErrorMessage ?? "")}");
            }

            sb.AppendLine();
            sb.AppendLine("===== 迁移摘要 =====");
            sb.AppendLine($"年份：{_parsedYear}");
            sb.AppendLine($"源目录：{SourceDirectory}");
            sb.AppendLine($"目标目录：{TargetDirectory}");
            sb.AppendLine($"迁移时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"总文件数：{total}");
            sb.AppendLine($"已移动：{movedCount}");
            sb.AppendLine($"跳过：{skippedCount}");
            sb.AppendLine($"错误：{errorCount}");

            File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
            return filePath;
        }
        catch (Exception ex)
        {
            AddLog($"报告生成失败：{ex.Message}", true);
            return "";
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

        try
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Logs.Add(log);
                while (Logs.Count > 1000)
                    Logs.RemoveAt(0);
            });
        }
        catch (Exception)
        {
            // 忽略日志添加失败
        }
    }
}
