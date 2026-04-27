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

    private record MigrationRecord(string SourcePath, string TargetPath, long Size, string Status, string? ErrorMessage);

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

    partial void OnYearTextChanged(string value)
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

            var allFiles = Directory.EnumerateFiles(SourceDirectory, "*.*", SearchOption.AllDirectories);
            int totalCount = 0;
            long totalSize = 0;

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
                        _filesToMove.Add(info);
                        totalSize += info.Length;
                    }

                    totalCount++;
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }

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

            int movedCount = 0;
            int skippedCount = 0;
            int errorCount = 0;
            int total = _filesToMove.Count;
            _migrationRecords.Clear();

            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();

                var file = _filesToMove[i];
                var targetPath = Path.Combine(yearSubDir, file.Name);

                try
                {
                    if (!File.Exists(file.FullName))
                    {
                        _migrationRecords.Add(new MigrationRecord(file.FullName, "", file.Length, "跳过", "文件不存在"));
                        skippedCount++;
                        AddLog($"跳过（文件不存在）：{file.Name}");
                        continue;
                    }

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
                    movedCount++;
                    _migrationRecords.Add(new MigrationRecord(file.FullName, targetPath, file.Length, "已移动", null));
                    AddLog($"已移动：{file.Name}");
                }
                catch (Exception ex)
                {
                    errorCount++;
                    _migrationRecords.Add(new MigrationRecord(file.FullName, "", file.Length, "错误", ex.Message));
                    AddLog($"错误：{file.Name} - {ex.Message}", true);
                }

                var completed = movedCount + skippedCount + errorCount;
                if (completed % 50 == 0 || completed == total)
                {
                    ProgressValue = (double)completed / total * 100;
                    StatusMessage = $"已处理 {completed}/{total}";
                }
            }

            StatusMessage = $"迁移完成 - 已移动：{movedCount}，跳过：{skippedCount}，错误：{errorCount}";
            MigrateButtonContent = "迁移完成";
            CanScan = true;
            CanMigrate = false;
            CanCancel = false;
            ProgressValue = 100;

            AddLog($"迁移完成！已移动：{movedCount}，跳过：{skippedCount}，错误：{errorCount}");

            // 生成迁移报告
            var reportPath = GenerateReportCsv(movedCount, skippedCount, errorCount, total);
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

    private string? GenerateReportCsv(int movedCount, int skippedCount, int errorCount, int total)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var defaultFileName = $"迁移报告_{_parsedYear}_{timestamp}.csv";

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
            sb.AppendLine("操作,源路径,目标路径,文件大小,错误信息");

            foreach (var record in _migrationRecords)
            {
                sb.AppendLine($"{EscapeCsv(record.Status)},{EscapeCsv(record.SourcePath)},{EscapeCsv(record.TargetPath)},{FormatSize(record.Size)},{EscapeCsv(record.ErrorMessage ?? "")}");
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

            File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
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
