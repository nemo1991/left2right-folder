using CommunityToolkit.Mvvm.ComponentModel;
using file_sync.Models;
using file_sync.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace file_sync.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IReportGenerator _reportGenerator;

    private CancellationTokenSource? _cts;

    [ObservableProperty] private string _sourceDirectory = "";
    [ObservableProperty] private string _targetDirectory = "";
    [ObservableProperty] private string _statusMessage = "就绪";
    [ObservableProperty] private double _progressValue;

    // 进度是否不可预测，比如正在扫描文件时无法确定总数，这时显示一个转圈的进度条
    [ObservableProperty]
    private bool _isProgressIndeterminate;

    [ObservableProperty] private bool _canScan = false;
    [ObservableProperty] private bool _canMigrate;
    [ObservableProperty] private bool _canCancel;
    [ObservableProperty] private string _scanButtonContent = "扫描目录";
    [ObservableProperty] private string _migrateButtonContent = "开始迁移";

    [ObservableProperty] private int _totalScanned;
    [ObservableProperty] private int _toDeleteCount;
    [ObservableProperty] private int _toMoveCount;
    [ObservableProperty] private int _conflictCount;
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private ObservableCollection<LogEntry> _logs = new();


    private readonly ILeft2Right _left2Right;

    public MainViewModel(
        ILeft2Right left2Right,
        IReportGenerator reportGenerator
        )
    {
        _left2Right = left2Right;
        _reportGenerator = reportGenerator;
    }

    partial void OnSourceDirectoryChanged(string value)
    {
        ToDeleteCount = 0;
        ToMoveCount = 0;
        ConflictCount = 0;
        TotalScanned = 0;
        ErrorCount = 0;
        CanScan = !string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(TargetDirectory);
    }
    partial void OnTargetDirectoryChanged(string value)
    {
        ToDeleteCount = 0;
        ToMoveCount = 0;
        ConflictCount = 0;
        TotalScanned = 0;
        ErrorCount = 0;
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


        TotalScanned = 0;
        ToDeleteCount = 0;
        ToMoveCount = 0;
        ConflictCount = 0;
        ErrorCount = 0;
        StatusMessage = "正在扫描源目录...";
        ScanButtonContent = "扫描中...";
        MigrateButtonContent = "开始迁移";
        IsProgressIndeterminate = true;
        Logs.Clear();
        AddLog("开始进行扫描");

        // 情况之前的处理结果，准备新的扫描
        _left2Right.ItemInfos.Clear();

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            CanScan = false;
            CanMigrate = false;
            CanCancel = true;

            StatusMessage = $"扫描源目录：{Path.GetFileName(SourceDirectory)}";
            _left2Right.Left = SourceDirectory;
            _left2Right.Right = TargetDirectory;
            _left2Right.OnItemScaning += (s, e) =>
            {
                StatusMessage = $"当前文件：{Path.GetFileName(e.Current!.Source.FullPath)}";

                TotalScanned = e.ProcessedTotal;
                ToDeleteCount = e.ScanResultCount![ScanResult.ToDelete];
                ToMoveCount = e.ScanResultCount[ScanResult.ToMove];
                ConflictCount = e.ScanResultCount[ScanResult.Confilct];
                ErrorCount = e.ScanResultCount[ScanResult.Error];

                if (e.IsErr)
                {
                    AddLog($"错误：{e.Current.Source.FullPath} - {e.Msg}", true);
                }
            };

            await _left2Right.ScanAsync(ct);

            StatusMessage = $"扫描完成";
            ScanButtonContent = "重新扫描";

            CanScan = true;
            CanMigrate = ToDeleteCount > 0 || ToMoveCount > 0;
            CanCancel = false;

            IsProgressIndeterminate = false;
            ProgressValue = 100;
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

        AddLog("扫描结束");
    }

    public async Task MigrateAsync()
    {
        _cts = new CancellationTokenSource();

        CanMigrate = false;
        CanScan = false;
        CanCancel = true;
        IsProgressIndeterminate = false;
        ProgressValue = 0;
        StatusMessage = "正在迁移文件...";
        MigrateButtonContent = "迁移中...";
        var beign = DateTime.Now;


        try
        {
            var progress = new Progress<LogEntry>(Logs.Add);

            _left2Right.OnItemMigrating += (s, e) =>
            {
                ProgressValue = (double)e.ProcessedTotal / e.Total * 100;
                StatusMessage = $"已迁移文件：{Path.GetFileName(e.Current!.Source.FullPath)}; {e.ProcessedTotal}/{e.Total}";
                if (e.IsErr)
                {
                    ((IProgress<LogEntry>)progress).Report(new LogEntry($"文件：{Path.GetFileName(e.Current!.Source.FullPath)}，迁移遇到问题：{e.Msg!}", true));
                }

            };

            await _left2Right.MigrateAsync(_cts.Token);

            //// 生成报告
            var report = new MigrationReport(
                beign,
                DateTime.Now,
                SourceDirectory,
                TargetDirectory,
                _left2Right
            );

            var reportPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"文件迁移报告_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            await _reportGenerator.GenerateCsvAsync(report, reportPath);

            StatusMessage = $"迁移完成 - 删除：{_left2Right.ItemInfos.Count(i => i.MigrateResult == MigrateResult.Deleted)}, 移动：{_left2Right.ItemInfos.Count(i => i.MigrateResult == MigrateResult.Moved)}, 跳过：{_left2Right.ItemInfos.Count(i => i.MigrateResult == MigrateResult.Skipped)}, 失败：{_left2Right.ItemInfos.Count(i => i.MigrateResult == MigrateResult.Fail)}";

            MigrateButtonContent = "迁移完成";

            AddLog($"迁移完成！报告已保存到：{reportPath}");

        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消迁移";
            AddLog("迁移已取消");
        }
        catch (Exception ex)
        {
            StatusMessage = $"迁移失败：{ex.Message}";
            AddLog($"错误：{ex.Message}", true);
        }

        CanScan = true;
        MigrateButtonContent = "开始迁移";
        CanMigrate = false;
        CanCancel = false;
    }

    public void Cancel()
    {
        _cts?.Cancel();
        StatusMessage = "正在取消...";
    }

    private void AddLog(string message, bool isError = false)
    {
        Logs.Add(new LogEntry(message, isError));
    }
}

public record LogEntry
{
    public string Message { get; init; }
    public bool IsError { get; init; }

    public LogEntry(string message, bool isError, string timestamp)
    {
        Message = $"[{timestamp}] {(isError ? "X " : "")}{message}";
        IsError = isError;
    }

    public LogEntry(string message, bool isError) : this(message, isError, DateTime.Now.ToString("HH:mm:ss"))
    {

    }

    public LogEntry(string message) : this(message, false, DateTime.Now.ToString("HH:mm:ss"))
    {

    }
}
