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
    public async Task<file_sync.Models.CompareResult> CompareAsync(
        List<FileEntry> sourceFiles,
        string targetDirectory,
        IHashCalculator hashCalculator,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var toDelete = new List<FileEntryToDelete>();
        var toMove = new List<FileEntry>();
        var conflicts = new List<FileEntry>();

        foreach (var sourceFile in sourceFiles)
        {
            ct.ThrowIfCancellationRequested();

            // 检查目标目录是否有同名文件
            var targetPath = Path.Combine(targetDirectory, sourceFile.FileName);
            var targetExists = File.Exists(targetPath);

            if (targetExists)
            {
                // 计算源文件 Hash
                var sourceHash = await hashCalculator.ComputeHashAsync(sourceFile.FullPath, null, ct);

                // 计算目标文件 Hash 并比较
                var targetHash = await hashCalculator.ComputeHashAsync(targetPath, null, ct);

                if (sourceHash == targetHash)
                {
                    // Hash 一致，标记删除
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
                    progress?.Report($"待删除：{sourceFile.FileName} (目标目录存在相同文件)");
                }
                else
                {
                    // Hash 不一致，标记冲突
                    conflicts.Add(sourceFile with { Status = FileStatus.Error, Hash = sourceHash });
                    progress?.Report($"冲突：{sourceFile.FileName} (同名但内容不同)");
                }
            }
            else
            {
                // 目标目录没有同名文件，标记移动
                toMove.Add(sourceFile with { Status = FileStatus.ToMove });
                progress?.Report($"待移动：{sourceFile.FileName}");
            }
        }

        return new file_sync.Models.CompareResult(toDelete, toMove, sourceFiles.Count, toDelete.Count + toMove.Count + conflicts.Count);
    }
}
