using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        List<FileEntry> targetFiles,
        IHashCalculator hashCalculator,
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}

/// <summary>
/// 文件对比服务 - 先文件名过滤，再 Hash 精确比对
/// </summary>
public class FileComparator : IFileComparator
{
    public async Task<file_sync.Models.CompareResult> CompareAsync(
        List<FileEntry> sourceFiles,
        List<FileEntry> targetFiles,
        IHashCalculator hashCalculator,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        // 按文件名分组目标文件
        var targetByFileName = targetFiles
            .GroupBy(f => f.FileName)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Hash 缓存 - 避免重复计算同一文件的 Hash
        var hashCache = new ConcurrentDictionary<string, string>();

        var toDelete = new List<FileEntryToDelete>();
        var toMove = new List<FileEntry>();

        foreach (var sourceFile in sourceFiles)
        {
            ct.ThrowIfCancellationRequested();

            if (targetByFileName.TryGetValue(sourceFile.FileName, out var targets))
            {
                // 文件名相同，进一步比较 Hash
                // 优化：先比较文件大小，大小相同再计算 Hash
                var sameSizeTargets = targets.Where(t => t.FileSize == sourceFile.FileSize).ToList();

                if (sameSizeTargets.Count > 0)
                {
                    // 计算源文件 Hash
                    var sourceHash = await hashCalculator.ComputeHashAsync(sourceFile.FullPath, null, ct);

                    // 检查是否有相同 Hash 的文件
                    bool isDuplicate = false;
                    FileEntry? matchingTarget = null;
                    foreach (var target in sameSizeTargets)
                    {
                        // 使用缓存避免重复计算
                        string? targetHash;
                        if (!hashCache.TryGetValue(target.FullPath, out targetHash))
                        {
                            targetHash = await hashCalculator.ComputeHashAsync(target.FullPath, null, ct);
                            hashCache.TryAdd(target.FullPath, targetHash);
                        }

                        if (sourceHash == targetHash)
                        {
                            isDuplicate = true;
                            matchingTarget = target;
                            break;
                        }
                    }

                    if (isDuplicate && matchingTarget != null)
                    {
                        toDelete.Add(new FileEntryToDelete(
                            sourceFile with { Status = FileStatus.ToDelete, Hash = sourceHash },
                            matchingTarget
                        ));
                        progress?.Report($"待删除：{sourceFile.FileName} (目标目录已存在)");
                        continue;
                    }
                }

                // 文件名相同但 Hash 不同，需要移动（覆盖由用户决定，这里先标记为移动）
                toMove.Add(sourceFile with { Status = FileStatus.ToMove });
                progress?.Report($"待移动：{sourceFile.FileName} (文件名冲突但内容不同)");
            }
            else
            {
                // 文件名不存在于目标目录，直接移动
                toMove.Add(sourceFile with { Status = FileStatus.ToMove });
                progress?.Report($"待移动：{sourceFile.FileName}");
            }
        }

        return new file_sync.Models.CompareResult(toDelete, toMove, sourceFiles.Count, targetFiles.Count);
    }
}
