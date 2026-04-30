using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace file_sync.Services;

/// <summary>
/// Hash 计算服务接口
/// </summary>
public interface IHashCalculator
{
    Task<string> ComputeHashAsync(string filePath, IProgress<double>? progress = null, CancellationToken ct = default);
}

/// <summary>
/// Hash 计算服务 - 流式计算 MD5
/// </summary>
public class HashCalculator : IHashCalculator
{
    private const int BufferSize = 1024 * 1024; // 1MB 缓冲区

    public async Task<string> ComputeHashAsync(string filePath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        try
        {
            return await Task.Run(() =>
            {
                using var md5 = System.Security.Cryptography.MD5.Create();
                using var stream = File.OpenRead(filePath);
                var buffer = new byte[BufferSize];
                long totalRead = 0;

                int bytesRead;
                while ((bytesRead = stream.Read(buffer, 0, BufferSize)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    md5.TransformBlock(buffer, 0, bytesRead, null, 0);
                    totalRead += bytesRead;
                    progress?.Report((double)totalRead / stream.Length * 100);
                }

                md5.TransformFinalBlock(buffer, 0, 0);
                return BitConverter.ToString(md5.Hash!).Replace("-", "").ToLower();
            }, ct);
        }
        catch (AggregateException ae)
        {
            if (ae.InnerException is OperationCanceledException)
                throw ae.InnerException;
            throw;
        }
    }
}
