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
        List<FileEntryToDelete> toDelete,
        List<FileEntry> toMove,
        List<FileEntry> conflicts,
        string sourceDirectory,
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
    public Task<MigrationResult> MigrateAsync(
        List<FileEntryToDelete> toDelete,
        List<FileEntry> toMove,
        List<FileEntry> conflicts,
        string sourceDirectory,
        string targetDirectory,
        IProgress<MigrationProgress>? progress = null,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var details = new List<MigrationDetail>();
            int deletedCount = 0;
            int migratedCount = 0;
            int skippedCount = conflicts.Count;
            int errorCount = 0;

            // 先记录冲突文件
            foreach (var conflict in conflicts)
            {
                details.Add(new MigrationDetail(
                    "Conflict", conflict.FullPath, "", conflict.FileSize, conflict.Hash,
                    conflict.CreatedTime, conflict.LastModified, conflict.LastAccessTime,
                    "Skipped", "文件名冲突 - 内容不同",
                    0, "", default, default, default
                ));
            }

            // 先执行删除操作
            foreach (var fileEntry in toDelete)
            {
                ct.ThrowIfCancellationRequested();

                var file = fileEntry.SourceFile;
                var targetFile = fileEntry.TargetFile;

                try
                {
                    if (File.Exists(file.FullPath))
                    {
                        File.Delete(file.FullPath);
                        deletedCount++;
                        details.Add(new MigrationDetail(
                            "Delete", file.FullPath, targetFile.FullPath, file.FileSize, file.Hash,
                            file.CreatedTime, file.LastModified, file.LastAccessTime,
                            "Success", "",
                            targetFile.FileSize, targetFile.Hash,
                            targetFile.CreatedTime, targetFile.LastModified, targetFile.LastAccessTime
                        ));
                    }
                    else
                    {
                        skippedCount++;
                        details.Add(new MigrationDetail(
                            "Delete", file.FullPath, targetFile.FullPath, file.FileSize, file.Hash,
                            file.CreatedTime, file.LastModified, file.LastAccessTime,
                            "Skipped", "文件不存在",
                            targetFile.FileSize, targetFile.Hash,
                            targetFile.CreatedTime, targetFile.LastModified, targetFile.LastAccessTime
                        ));
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    details.Add(new MigrationDetail(
                        "Delete", file.FullPath, targetFile.FullPath, file.FileSize, file.Hash,
                        file.CreatedTime, file.LastModified, file.LastAccessTime,
                        "Failed", ex.Message,
                        targetFile.FileSize, targetFile.Hash,
                        targetFile.CreatedTime, targetFile.LastModified, targetFile.LastAccessTime
                    ));
                }

                progress?.Report(new MigrationProgress(file.FullPath, deletedCount + migratedCount, toDelete.Count + toMove.Count + conflicts.Count, "删除"));
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
                            "Move", file.FullPath, "", file.FileSize, file.Hash, file.CreatedTime, file.LastModified, file.LastAccessTime, "Skipped", "文件不存在",
                            0, "", default, default, default
                        ));
                        continue;
                    }

                    var relativePath = Path.GetRelativePath(sourceDirectory, file.FullPath);
                    var targetPath = Path.Combine(targetDirectory, relativePath);

                    var targetDir = Path.GetDirectoryName(targetPath)!;
                    Directory.CreateDirectory(targetDir);

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

                    File.Move(file.FullPath, targetPath);
                    migratedCount++;
                    details.Add(new MigrationDetail(
                        "Move", file.FullPath, targetPath, file.FileSize, file.Hash, file.CreatedTime, file.LastModified, file.LastAccessTime, "Success", "",
                        0, "", default, default, default
                    ));
                }
                catch (Exception ex)
                {
                    errorCount++;
                    details.Add(new MigrationDetail(
                        "Move", file.FullPath, "", file.FileSize, file.Hash, file.CreatedTime, file.LastModified, file.LastAccessTime, "Failed", ex.Message,
                        0, "", default, default, default
                    ));
                }

                progress?.Report(new MigrationProgress(file.FullPath, deletedCount + migratedCount, toDelete.Count + toMove.Count + conflicts.Count, "移动"));
            }

            return new MigrationResult(deletedCount, migratedCount, skippedCount, errorCount, details);
        }, ct);
    }
}
