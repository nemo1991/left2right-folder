using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly IAppState _appState;

    private CancellationTokenSource? _cts;
    private System.Collections.Generic.List<FileEntry> _sourceFiles = new();
    private System.Collections.Generic.List<FileEntry> _targetFiles = new();
    private CompareResult? _compareResult;
    private string? _sessionId;

    [ObservableProperty] private string _sourceDirectory = "";
    [ObservableProperty] private string _targetDirectory = "";
    [ObservableProperty] private string _statusMessage = "就绪";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private bool _isProgressIndeterminate;
    [ObservableProperty] private bool _canScan = true;
    [ObservableProperty] private bool _canMigrate;
    [ObservableProperty] private string _scanButtonContent = "扫描目录";
    [ObservableProperty] private string _migrateButtonContent = "开始迁移";

    [ObservableProperty] private int _totalScanned;
    [ObservableProperty] private int _toDeleteCount;
    [ObservableProperty] private int _toMoveCount;
    [ObservableProperty] private ObservableCollection<LogEntry> _logs = new();

    public MainViewModel(
        IFileScanner fileScanner,
        IHashCalculator hashCalculator,
        IFileComparator fileComparator,
        IFileMigrator fileMigrator,
        IReportGenerator reportGenerator,
        IAppState appState)
    {
        _fileScanner = fileScanner;
        _hashCalculator = hashCalculator;
        _fileComparator = fileComparator;
        _fileMigrator = fileMigrator;
        _reportGenerator = reportGenerator;
        _appState = appState;
    }

    partial void OnSourceDirectoryChanged(string value) => CanScan = !string.IsNullOrEmpty(value);
    partial void OnTargetDirectoryChanged(string value) => CanScan = !string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(SourceDirectory);

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async void ScanDirectoriesAsync()
    {
        if (string.IsNullOrEmpty(SourceDirectory) || string.IsNullOrEmpty(TargetDirectory))
            return;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            CanScan = false;
            CanMigrate = false;
            IsProgressIndeterminate = true;
            StatusMessage = "正在扫描源目录...";
            ScanButtonContent = "扫描中...";
            Logs.Clear();
            _sourceFiles.Clear();
            _targetFiles.Clear();

            // 生成会话 ID
            _sessionId = Guid.NewGuid().ToString();
            await _appState.SaveScanSessionAsync(_sessionId, SourceDirectory, TargetDirectory);

            // 扫描源目录
            var sourceProgress = new Progress<string>(file =>
            {
                StatusMessage = $"扫描源目录：{Path.GetFileName(file)}";
            });
            _sourceFiles = await _fileScanner.ScanAsync(SourceDirectory, sourceProgress, ct);
            TotalScanned = _sourceFiles.Count;

            AddLog($"源目录扫描完成：{_sourceFiles.Count} 个文件");

            // 扫描目标目录
            StatusMessage = "正在扫描目标目录...";
            var targetProgress = new Progress<string>(file =>
            {
                StatusMessage = $"扫描目标目录：{Path.GetFileName(file)}";
            });
            _targetFiles = await _fileScanner.ScanAsync(TargetDirectory, targetProgress, ct);

            AddLog($"目标目录扫描完成：{_targetFiles.Count} 个文件");

            // 对比文件
            StatusMessage = "正在对比文件...";
            var compareProgress = new Progress<string>(msg =>
            {
                AddLog(msg);
            });
            _compareResult = await _fileComparator.CompareAsync(_sourceFiles, _targetFiles, _hashCalculator, compareProgress, ct);

            ToDeleteCount = _compareResult.ToDelete.Count;
            ToMoveCount = _compareResult.ToMove.Count;

            StatusMessage = $"扫描完成 - 待删除：{ToDeleteCount}, 待移动：{ToMoveCount}";
            ScanButtonContent = "重新扫描";
            CanScan = true;
            CanMigrate = true;
            IsProgressIndeterminate = false;
            ProgressValue = 100;

            AddLog($"对比完成 - 待删除：{ToDeleteCount}, 待移动：{ToMoveCount}");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消扫描";
            ScanButtonContent = "扫描目录";
            CanScan = true;
            IsProgressIndeterminate = false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"扫描失败：{ex.Message}";
            ScanButtonContent = "扫描目录";
            CanScan = true;
            IsProgressIndeterminate = false;
            AddLog($"错误：{ex.Message}", true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanMigrate))]
    private async void MigrateAsync()
    {
        if (_compareResult == null) return;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            CanMigrate = false;
            CanScan = false;
            IsProgressIndeterminate = false;
            ProgressValue = 0;
            StatusMessage = "正在迁移文件...";
            MigrateButtonContent = "迁移中...";

            var progress = new Progress<MigrationProgress>(p =>
            {
                ProgressValue = (double)p.Completed / p.Total * 100;
                StatusMessage = $"{p.Operation}: {p.Completed}/{p.Total} - {Path.GetFileName(p.CurrentFile)}";
                AddLog($"{p.Operation}: {Path.GetFileName(p.CurrentFile)}");
            });

            var result = await _fileMigrator.MigrateAsync(
                _compareResult.ToDelete,
                _compareResult.ToMove,
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
                result.SkippedCount,
                result.ErrorCount,
                result.Details
            );

            var reportPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"迁移报告_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            await _reportGenerator.GenerateCsvAsync(report, reportPath);

            StatusMessage = $"迁移完成 - 删除：{result.DeletedCount}, 移动：{result.MigratedCount}, 错误：{result.ErrorCount}";
            MigrateButtonContent = "迁移完成";
            CanScan = true;

            AddLog($"迁移完成！报告已保存到：{reportPath}");

            // 标记会话完成
            if (_sessionId != null)
            {
                await _appState.MarkSessionCompletedAsync(_sessionId);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消迁移";
            MigrateButtonContent = "开始迁移";
            CanMigrate = true;
            CanScan = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"迁移失败：{ex.Message}";
            MigrateButtonContent = "开始迁移";
            CanMigrate = true;
            CanScan = true;
            AddLog($"错误：{ex.Message}", true);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        StatusMessage = "正在取消...";
    }

    [RelayCommand]
    private void BrowseSource()
    {
        RequestFolderSelection?.Invoke(this, "source");
    }

    [RelayCommand]
    private void BrowseTarget()
    {
        RequestFolderSelection?.Invoke(this, "target");
    }

    public event EventHandler<string>? RequestFolderSelection;

    private void AddLog(string message, bool isError = false)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var log = new LogEntry($"[{timestamp}] {(isError ? "X " : "")}{message}", isError);

        // 在 UI 线程添加
        Application.Current?.Dispatcher.Invoke(() =>
        {
            Logs.Insert(0, log);
            // 限制日志数量
            while (Logs.Count > 1000)
            {
                Logs.RemoveAt(Logs.Count - 1);
            }
        });
    }
}

public record LogEntry(string Message, bool IsError);
