using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using file_sync.Models;

namespace file_sync.Services;

/// <summary>
/// 文件对比服务接口
/// </summary>
public interface IFileComparator
{
    Task<file_sync.Models.CompareResult> CompareAsync(
        List<FileEntry> sourceFiles,
        string targetDirectory,
        IHashCalculator hashCalculator,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}

/// <summary>
/// 文件对比服务 - 仅检查同名文件，不缓存目标目录
/// </summary>
public class FileComparator : IFileComparator
{
    public Task<file_sync.Models.CompareResult> CompareAsync(
        List<FileEntry> sourceFiles,
        string targetDirectory,
        IHashCalculator hashCalculator,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        return Task.Run(async () =>
        {
            var toDelete = new List<FileEntryToDelete>();
            var toMove = new List<FileEntry>();
            var conflicts = new List<FileEntry>();
            int count = 0;
            const int ReportInterval = 100;

            foreach (var sourceFile in sourceFiles)
            {
                ct.ThrowIfCancellationRequested();

                var targetPath = Path.Combine(targetDirectory, sourceFile.FileName);
                var targetExists = File.Exists(targetPath);

                if (targetExists)
                {
                    var sourceHash = await hashCalculator.ComputeHashAsync(sourceFile.FullPath, null, ct);
                    var targetHash = await hashCalculator.ComputeHashAsync(targetPath, null, ct);

                    if (sourceHash == targetHash)
                    {
                        var targetInfo = new FileInfo(targetPath);
                        var targetFile = new FileEntry(
                            targetPath,
                            targetInfo.Name,
                            targetInfo.Length,
                            targetInfo.LastWriteTime,
                            targetInfo.CreationTime,
                            targetInfo.LastAccessTime,
                            targetHash
                        );

                        toDelete.Add(new FileEntryToDelete(
                            sourceFile with { Status = FileStatus.ToDelete, Hash = sourceHash },
                            targetFile
                        ));
                        if (++count % ReportInterval == 0)
                            progress?.Report($"已对比 {count}/{sourceFiles.Count} 个文件");
                    }
                    else
                    {
                        conflicts.Add(sourceFile with { Status = FileStatus.Error, Hash = sourceHash });
                        if (++count % ReportInterval == 0)
                            progress?.Report($"已对比 {count}/{sourceFiles.Count} 个文件");
                    }
                }
                else
                {
                    toMove.Add(sourceFile with { Status = FileStatus.ToMove });
                    if (++count % ReportInterval == 0)
                        progress?.Report($"已对比 {count}/{sourceFiles.Count} 个文件");
                }
            }

            if (progress != null && sourceFiles.Count > 0)
            {
                progress.Report($"对比完成，共 {sourceFiles.Count} 个文件");
            }

            return new file_sync.Models.CompareResult(toDelete, toMove, conflicts, sourceFiles.Count, toDelete.Count + toMove.Count + conflicts.Count);
        }, ct);
    }
}
