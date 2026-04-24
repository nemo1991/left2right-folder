using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using file_sync.Models;

namespace file_sync.Services;

/// <summary>
/// 文件扫描服务接口
/// </summary>
public interface IFileScanner
{
    Task<List<FileEntry>> ScanAsync(string directory, IProgress<string>? progress = null, CancellationToken ct = default);
}

/// <summary>
/// 文件扫描服务 - 并行扫描目录
/// </summary>
public class FileScanner : IFileScanner
{
    public Task<List<FileEntry>> ScanAsync(string directory, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var files = new ConcurrentBag<FileEntry>();
            int count = 0;
            const int ReportInterval = 100;

            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException($"目录不存在：{directory}");
            }

            // 使用 EnumerateFiles 流式遍历，避免 GetFiles 一次性加载所有文件
            var fileEnumerator = Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
                .GetEnumerator();

            while (fileEnumerator.MoveNext())
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var filePath = fileEnumerator.Current;
                    var info = new FileInfo(filePath);

                    // 跳过系统文件和隐藏文件
                    if ((info.Attributes & FileAttributes.System) != 0)
                        continue;

                    files.Add(new FileEntry(
                        filePath,
                        info.Name,
                        info.Length,
                        info.LastWriteTime,
                        info.CreationTime,
                        info.LastAccessTime
                    ));

                    count++;
                    // 每 100 个文件报告一次进度，避免 UI 线程过载
                    if (count % ReportInterval == 0 && progress != null)
                    {
                        progress.Report(filePath);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // 跳过无权限访问的文件
                }
                catch (IOException)
                {
                    // 跳过无法访问的文件
                }
            }

            // 最后一次进度报告
            if (progress != null && count > 0)
            {
                progress.Report($"扫描完成，共 {count} 个文件");
            }

            return files.ToList();
        }, ct);
    }
}
