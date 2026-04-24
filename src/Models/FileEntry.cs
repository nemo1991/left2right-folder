using System.Collections.Generic;

namespace file_sync.Models;

/// <summary>
/// 文件条目信息
/// </summary>
public record FileEntry(
    string FullPath,
    string FileName,
    long FileSize,
    System.DateTime LastModified,
    System.DateTime CreatedTime,
    string Hash = "",
    FileStatus Status = FileStatus.Pending
);

/// <summary>
/// 文件状态
/// </summary>
public enum FileStatus
{
    Pending,        // 待处理
    Scanned,        // 已扫描
    ToDelete,       // 待删除（目标目录已存在）
    ToMove,         // 待移动（目标目录不存在）
    Migrated,       // 已迁移
    Skipped,        // 已跳过
    Error           // 错误
}

/// <summary>
/// 对比结果
/// </summary>
public record CompareResult(
    List<FileEntry> ToDelete,
    List<FileEntry> ToMove,
    int TotalSource,
    int TotalTarget
);

/// <summary>
/// 迁移报告
/// </summary>
public record MigrationReport(
    System.DateTime StartTime,
    System.DateTime EndTime,
    string SourceDirectory,
    string TargetDirectory,
    int TotalScanned,
    int DeletedCount,
    int MigratedCount,
    int SkippedCount,
    int ErrorCount,
    List<MigrationDetail> Details
);

/// <summary>
/// 迁移详细信息
/// </summary>
public record MigrationDetail(
    string Operation,     // Delete / Move / Skip / Error
    string SourcePath,
    string TargetPath,
    long FileSize,
    string Hash,
    System.DateTime CreatedTime,
    string Status,        // Success / Failed / Skipped
    string ErrorMessage
);
