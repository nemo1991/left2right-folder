using System;
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
