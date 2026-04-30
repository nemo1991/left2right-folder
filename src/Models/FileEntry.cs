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
    System.DateTime LastAccessTime,
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
/// 待删除文件（包含目标目录中对应的文件信息）
/// </summary>
public record FileEntryToDelete(
    FileEntry SourceFile,
    FileEntry TargetFile  // 目标目录中对应的文件
);

/// <summary>
/// 对比结果
/// </summary>
public record CompareResult(
    List<FileEntryToDelete> ToDelete,
    List<FileEntry> ToMove,
    List<FileEntry> Conflicts,  // 冲突文件（同名但 Hash 不同）
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
    int ErrorCount,
    int ConflictCount,
    List<MigrationDetail> Details
);

/// <summary>
/// 迁移详细信息
/// </summary>
public record MigrationDetail(
    string Operation,     // Delete / Move / Skip / Error
    string SourcePath,
    string TargetPath,    // 移动操作的目标路径 / 删除操作中目标目录已存在的文件路径
    long FileSize,
    string Hash,
    System.DateTime CreatedTime,
    System.DateTime LastModified,
    System.DateTime LastAccessed,
    string Status,        // Success / Failed / Skipped
    string ErrorMessage,
    long TargetFileSize = 0,         // 目标文件大小（仅删除操作）
    string TargetHash = "",          // 目标文件 Hash（仅删除操作）
    System.DateTime TargetCreatedTime = default,  // 目标文件创建时间（仅删除操作）
    System.DateTime TargetLastModified = default, // 目标文件最后修改时间（仅删除操作）
    System.DateTime TargetLastAccessed = default  // 目标文件最后访问时间（仅删除操作）
);
