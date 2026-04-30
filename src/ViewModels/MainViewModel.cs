using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using file_sync.Models;
using file_sync.Services;

namespace file_sync.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IFileScanner _fileScanner;
    private readonly IHashCalculator _hashCalculator;
    private readonly IFileComparator _fileComparator;
    private readonly IFileMigrator _fileMigrator;
    private readonly IReportGenerator _reportGenerator;

    private CancellationTokenSource? _cts;
    private List<FileEntry> _sourceFiles = new();
    private CompareResult? _compareResult;

    [ObservableProperty] private string _sourceDirectory = "";
    [ObservableProperty] private string _targetDirectory = "";
    [ObservableProperty] private string _statusMessage = "就绪";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isProgressIndeterminate;
    [ObservableProperty] private bool _canScan = false;
    [ObservableProperty] private bool _canMigrate;
    [ObservableProperty] private bool _canCancel;
    [ObservableProperty] private string _scanButtonContent = "扫描目录";
    [ObservableProperty] private string _migrateButtonContent = "开始迁移";

    [ObservableProperty] private int _totalScanned;
    [ObservableProperty] private int _toDeleteCount;
    [ObservableProperty] private int _toMoveCount;
    [ObservableProperty] private int _conflictCount;
    [ObservableProperty] private ObservableCollection<LogEntry> _logs = new();


    public MainViewModel(
        IFileScanner fileScanner,
        IHashCalculator hashCalculator,
        IFileComparator fileComparator,
        IFileMigrator fileMigrator,
        IReportGenerator reportGenerator
        )
    {
        _fileScanner = fileScanner;
        _hashCalculator = hashCalculator;
        _fileComparator = fileComparator;
        _fileMigrator = fileMigrator;
        _reportGenerator = reportGenerator;
    }

    partial void OnSourceDirectoryChanged(string value)
    {
        ToDeleteCount = 0;
        ToMoveCount = 0;
        ConflictCount = 0;
        TotalScanned = 0;
        CanScan = !string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(TargetDirectory);
    }
    partial void OnTargetDirectoryChanged(string value)
    {
        ToDeleteCount = 0;
        ToMoveCount = 0;
        ConflictCount = 0;
        TotalScanned = 0;
        CanScan = !string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(SourceDirectory);
    }

    public async Task ScanDirectoriesAsync()
    {
        if (string.IsNullOrEmpty(SourceDirectory) || string.IsNullOrEmpty(TargetDirectory))
        {
            AddLog("错误：请先选择原目录和目标目录", true);
            return;
        }

        if (!Directory.Exists(SourceDirectory))
        {
            AddLog($"错误：原目录不存在：{SourceDirectory}", true);
            return;
        }

        if (!Directory.Exists(TargetDirectory))
        {
            AddLog($"错误：目标目录不存在：{TargetDirectory}", true);
            return;
        }

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            CanScan = false;
            CanMigrate = false;
            CanCancel = true;
            IsProgressIndeterminate = true;
            StatusMessage = "正在扫描源目录...";
            ScanButtonContent = "扫描中...";
            MigrateButtonContent = "开始迁移";
            Logs.Clear();
            _sourceFiles.Clear();
            TotalScanned = 0;
            ToDeleteCount = 0;
            ToMoveCount = 0;
            ConflictCount = 0;


            // 扫描源目录
            var sourceProgress = new Progress<string>(file =>
            {
                StatusMessage = $"扫描源目录：{Path.GetFileName(file)}";
            });
            _sourceFiles = await _fileScanner.ScanAsync(SourceDirectory, sourceProgress, ct);
            TotalScanned = _sourceFiles.Count;

            AddLog($"源目录扫描完成：{_sourceFiles.Count} 个文件");

            // 对比文件（不扫描目标目录，仅检查同名文件）
            StatusMessage = "正在对比文件...";
            var compareProgress = new Progress<string>(msg =>
            {
                AddLog(msg);
            });
            _compareResult = await _fileComparator.CompareAsync(_sourceFiles, SourceDirectory, TargetDirectory, _hashCalculator, compareProgress, ct);

            ToDeleteCount = _compareResult.ToDelete.Count;
            ToMoveCount = _compareResult.ToMove.Count;
            //_conflicts = _compareResult.Conflicts;
            ConflictCount = _compareResult.Conflicts.Count;

            StatusMessage = $"扫描完成 - 待删除：{ToDeleteCount}, 待移动：{ToMoveCount}, 冲突：{ConflictCount}";
            ScanButtonContent = "重新扫描";
            CanScan = true;
            CanMigrate = ToDeleteCount > 0 || ToMoveCount > 0;
            CanCancel = false;
            IsProgressIndeterminate = false;
            ProgressValue = 100;

            AddLog($"对比完成 - 待删除：{ToDeleteCount}, 待移动：{ToMoveCount}, 冲突：{ConflictCount}");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消扫描";
            ScanButtonContent = "扫描目录";
            CanScan = true;
            CanCancel = false;
            IsProgressIndeterminate = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"扫描失败：{ex.Message}";
            ScanButtonContent = "扫描目录";
            CanScan = true;
            CanCancel = false;
            IsProgressIndeterminate = false;
            AddLog($"错误：{ex.Message}", true);
        }
    }

    public async Task MigrateAsync()
    {
        if (_compareResult == null) return;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        MigrationResult? result = null;

        try
        {
            CanMigrate = false;
            CanScan = false;
            CanCancel = true;
            IsProgressIndeterminate = false;
            ProgressValue = 0;
            StatusMessage = "正在迁移文件...";
            MigrateButtonContent = "迁移中...";

            var progress = new Progress<MigrationProgress>(p =>
            {
                // 进度条每 50 个文件更新一次，避免 UI 过载
                if (p.Completed % 50 == 0 || p.Completed == 1 || p.Completed == p.Total)
                {
                    ProgressValue = (double)p.Completed / p.Total * 100;
                    StatusMessage = $"{p.Operation}: {p.Completed}/{p.Total}";
                }
                // 日志每条都记录
                AddLog($"{p.Operation}: {Path.GetFileName(p.CurrentFile)}");
            });

            result = await _fileMigrator.MigrateAsync(
                _compareResult.ToDelete,
                _compareResult.ToMove,
                _compareResult.Conflicts,
                SourceDirectory,
                TargetDirectory,
                progress,
                ct);

            // 生成报告
            var report = new MigrationReport(
                DateTime.Now,
                DateTime.Now,
                SourceDirectory,
                TargetDirectory,
                TotalScanned,
                result.DeletedCount,
                result.MigratedCount,
                result.ErrorCount,
                result.ConfilctCount,
                result.Details
            );

            var reportPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"文件迁移报告_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            await _reportGenerator.GenerateCsvAsync(report, reportPath);

            StatusMessage = $"迁移完成 - 删除：{result.DeletedCount}, 移动：{result.MigratedCount}, 冲突：{result.ConfilctCount}, 错误：{result.ErrorCount}";
            MigrateButtonContent = "迁移完成";
            CanScan = true;
            CanMigrate = false;
            CanCancel = false;

            AddLog($"迁移完成！报告已保存到：{reportPath}");

        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消迁移";
            MigrateButtonContent = "开始迁移";
            CanMigrate = true;
            CanScan = true;
            CanCancel = false;
            AddLog("迁移已取消");

            // 生成取消前的操作报告
            try
            {
                var cancelledReport = new MigrationReport(
                    DateTime.Now,
                    DateTime.Now,
                    SourceDirectory,
                    TargetDirectory,
                    TotalScanned,
                    result?.DeletedCount ?? 0,
                    result?.MigratedCount ?? 0,
                    result?.ErrorCount ?? 0,
                     result?.ConfilctCount ?? 0,
                    result?.Details ?? new List<MigrationDetail>()
                );
                var reportPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    $"文件迁移报告_已取消_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                await _reportGenerator.GenerateCsvAsync(cancelledReport, reportPath);
                AddLog($"取消报告已保存到：{reportPath}");
            }
            catch { }
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

    private void AddLog(string message, bool isError = false)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var log = new LogEntry($"[{timestamp}] {(isError ? "X " : "")}{message}", isError);

        // 在 UI 线程添加
        try
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                Logs.Add(log);
                while (Logs.Count > 1000)
                {
                    Logs.RemoveAt(0);
                }
            }));
        }
        catch (Exception)
        {
            // 忽略日志添加失败
        }
    }
}

public record LogEntry(string Message, bool IsError);
