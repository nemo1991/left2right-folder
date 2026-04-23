using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using file_sync.Models;

namespace file_sync.Services;

/// <summary>
/// 文件迁移服务接口
/// </summary>
public interface IFileMigrator
{
    Task<MigrationResult> MigrateAsync(
        List<FileEntry> toDelete,
        List<FileEntry> toMove,
        string targetDirectory,
        IProgress<MigrationProgress>? progress = null,
        CancellationToken ct = default);
}

public record MigrationResult(
    int DeletedCount,
    int MigratedCount,
    int SkippedCount,
    int ErrorCount,
    List<MigrationDetail> Details
);

public record MigrationProgress(
    string CurrentFile,
    int Completed,
    int Total,
    string Operation
);

/// <summary>
/// 文件迁移服务 - 执行删除/移动操作
/// </summary>
public class FileMigrator : IFileMigrator
{
    public async Task<MigrationResult> MigrateAsync(
        List<FileEntry> toDelete,
        List<FileEntry> toMove,
        string targetDirectory,
        IProgress<MigrationProgress>? progress = null,
        CancellationToken ct = default)
    {
        var details = new List<MigrationDetail>();
        int deletedCount = 0;
        int migratedCount = 0;
        int skippedCount = 0;
        int errorCount = 0;

        // 先执行删除操作
        foreach (var file in toDelete)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (File.Exists(file.FullPath))
                {
                    File.Delete(file.FullPath);
                    deletedCount++;
                    details.Add(new MigrationDetail(
                        "Delete", file.FullPath, "", file.FileSize, file.Hash, "Success", ""
                    ));
                }
                else
                {
                    skippedCount++;
                    details.Add(new MigrationDetail(
                        "Delete", file.FullPath, "", file.FileSize, file.Hash, "Skipped", "文件不存在"
                    ));
                }
            }
            catch (Exception ex)
            {
                errorCount++;
                details.Add(new MigrationDetail(
                    "Delete", file.FullPath, "", file.FileSize, file.Hash, "Failed", ex.Message
                ));
            }

            progress?.Report(new MigrationProgress(file.FullPath, deletedCount + migratedCount, toDelete.Count + toMove.Count, "删除"));
        }

        // 执行移动操作
        foreach (var file in toMove)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (!File.Exists(file.FullPath))
                {
                    skippedCount++;
                    details.Add(new MigrationDetail(
                        "Move", file.FullPath, "", file.FileSize, file.Hash, "Skipped", "文件不存在"
                    ));
                    continue;
                }

                // 计算目标路径
                var relativePath = GetRelativePath(file.FullPath, Path.GetPathRoot(file.FullPath)!);
                var targetPath = Path.Combine(targetDirectory, relativePath);

                // 确保目标目录存在
                var targetDir = Path.GetDirectoryName(targetPath)!;
                Directory.CreateDirectory(targetDir);

                // 如果目标文件已存在，添加后缀避免冲突
                if (File.Exists(targetPath))
                {
                    var baseName = Path.GetFileNameWithoutExtension(file.FileName);
                    var extension = Path.GetExtension(file.FileName);
                    var counter = 1;
                    do
                    {
                        targetPath = Path.Combine(targetDir, $"{baseName}_{counter}{extension}");
                        counter++;
                    } while (File.Exists(targetPath));
                }

                // 移动文件
                File.Move(file.FullPath, targetPath);
                migratedCount++;
                details.Add(new MigrationDetail(
                    "Move", file.FullPath, targetPath, file.FileSize, file.Hash, "Success", ""
                ));
            }
            catch (Exception ex)
            {
                errorCount++;
                details.Add(new MigrationDetail(
                    "Move", file.FullPath, "", file.FileSize, file.Hash, "Failed", ex.Message
                ));
            }

            progress?.Report(new MigrationProgress(file.FullPath, deletedCount + migratedCount, toDelete.Count + toMove.Count, "移动"));
        }

        return new MigrationResult(deletedCount, migratedCount, skippedCount, errorCount, details);
    }

    private string GetRelativePath(string fullPath, string rootPath)
    {
        if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(rootPath))
            return "";

        var relative = fullPath.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar);
        return relative;
    }
}
