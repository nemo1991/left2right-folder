using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;

namespace file_sync.Services;

/// <summary>
/// 基于前缀树的对象存储文件扫描器
/// 通过逐层展开目录树，只扫描与通配符匹配路径的文件
/// </summary>
public class ObjectStorageScanner
{
    private readonly IAmazonS3 _client;
    private readonly string _bucketName;
    private readonly string _wildcardPattern;
    private readonly string _basePrefix;
    private readonly CancellationToken _ct;

    /// <summary>
    /// 匹配的文件列表
    /// </summary>
    public List<ScannedFile> MatchedFiles { get; } = new();

    /// <summary>
    /// 扫描统计信息
    /// </summary>
    public ScanStatistics Statistics { get; private set; } = new();

    /// <summary>
    /// 扫描过程中发现的子目录（用于调试）
    /// </summary>
    public List<string> VisitedPrefixes { get; } = new();

    public ObjectStorageScanner(IAmazonS3 client, string bucketName, string basePrefix, string wildcardPattern, CancellationToken ct)
    {
        _client = client;
        _bucketName = bucketName;
        _basePrefix = basePrefix?.TrimEnd('/') ?? "";
        _wildcardPattern = wildcardPattern;
        _ct = ct;
    }

    /// <summary>
    /// 执行前缀树扫描
    /// </summary>
    public async Task ScanAsync(IProgress<string>? progress = null)
    {
        Statistics.StartTime = DateTime.Now;
        var regexPattern = WildcardToRegex(_wildcardPattern);

        // 如果通配符包含路径分隔符，从通配符中提取路径前缀
        var wildcardPathPrefix = ExtractPathPrefix(_wildcardPattern);
        var effectiveBasePrefix = CombinePrefixes(_basePrefix, wildcardPathPrefix);

        progress?.Report($"开始前缀树扫描，基础路径: {effectiveBasePrefix ?? "(根目录)"}");

        var queue = new Queue<string>();
        queue.Enqueue(effectiveBasePrefix ?? "");

        while (queue.Count > 0)
        {
            _ct.ThrowIfCancellationRequested();

            var currentPrefix = queue.Dequeue();
            VisitedPrefixes.Add(currentPrefix);
            Statistics.PrefixesScanned++;

            progress?.Report($"扫描目录: {currentPrefix}");

            var (files, subPrefixes) = await ListLevelAsync(currentPrefix);

            // 处理当前层的文件
            foreach (var file in files)
            {
                Statistics.FilesListed++;
                var fileName = GetFileName(file.Key, currentPrefix);

                if (MatchesPattern(fileName, regexPattern))
                {
                    MatchedFiles.Add(new ScannedFile
                    {
                        Key = file.Key,
                        Size = file.Size,
                        LastModified = file.LastModified,
                        MatchedPattern = _wildcardPattern
                    });
                    Statistics.FilesMatched++;
                    Statistics.TotalSize += file.Size;
                    progress?.Report($"  ✓ 匹配: {fileName} ({FormatSize(file.Size)})");
                }
            }

            // 将子目录加入队列继续扫描
            foreach (var subPrefix in subPrefixes)
            {
                // 检查子目录是否可能包含匹配的文件
                if (ShouldExplore(subPrefix, regexPattern))
                {
                    queue.Enqueue(subPrefix);
                }
            }
        }

        Statistics.EndTime = DateTime.Now;
        progress?.Report($"扫描完成！共扫描 {Statistics.PrefixesScanned} 个目录，列出 {Statistics.FilesListed} 个文件，匹配 {Statistics.FilesMatched} 个文件");
    }

    /// <summary>
    /// 列出指定层级的文件和子目录
    /// </summary>
    private async Task<(List<S3Object> Files, List<string> SubPrefixes)> ListLevelAsync(string prefix)
    {
        var request = new ListObjectsV2Request
        {
            BucketName = _bucketName,
            Prefix = prefix,
            Delimiter = "/"
        };

        var files = new List<S3Object>();
        var subPrefixes = new List<string>();

        ListObjectsV2Response response;
        do
        {
            _ct.ThrowIfCancellationRequested();
            response = await _client.ListObjectsV2Async(request, _ct);

            // 获取直接子文件
            foreach (var obj in response.S3Objects)
            {
                files.Add(obj);
            }

            // 获取子目录
            foreach (var subPrefix in response.CommonPrefixes)
            {
                subPrefixes.Add(subPrefix);
            }

            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated);

        return (files, subPrefixes);
    }

    /// <summary>
    /// 检查是否应该继续展开某个子目录
    /// </summary>
    private bool ShouldExplore(string subPrefix, System.Text.RegularExpressions.Regex pattern)
    {
        // 始终展开子目录，即使当前目录名不匹配
        // 因为匹配的文件可能在子目录深处
        return true;
    }

    /// <summary>
    /// 从通配符中提取路径前缀
    /// 例如: "photos/*.jpg" -> "photos/"
    ///       "backup/2024/*.zip" -> "backup/2024/"
    /// </summary>
    private static string ExtractPathPrefix(string pattern)
    {
        var lastSlash = pattern.LastIndexOf('/');
        return lastSlash >= 0 ? pattern.Substring(0, lastSlash + 1) : "";
    }

    /// <summary>
    /// 合并两个路径前缀
    /// </summary>
    private static string CombinePrefixes(string? basePrefix, string? subPrefix)
    {
        if (string.IsNullOrEmpty(basePrefix)) return subPrefix ?? "";
        if (string.IsNullOrEmpty(subPrefix)) return basePrefix;

        // 确保 basePrefix 以 / 结尾
        var bp = basePrefix.EndsWith("/") ? basePrefix : basePrefix + "/";
        return bp + subPrefix.TrimEnd('/');
    }

    /// <summary>
    /// 获取文件名（相对于当前前缀）
    /// </summary>
    private static string GetFileName(string fullKey, string currentPrefix)
    {
        if (string.IsNullOrEmpty(currentPrefix))
            return fullKey;

        return fullKey.StartsWith(currentPrefix)
            ? fullKey.Substring(currentPrefix.Length)
            : fullKey;
    }

    /// <summary>
    /// 通配符转正则表达式
    /// </summary>
    private static System.Text.RegularExpressions.Regex WildcardToRegex(string pattern)
    {
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return new System.Text.RegularExpressions.Regex(regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// 检查文件名是否匹配模式
    /// </summary>
    private static bool MatchesPattern(string fileName, System.Text.RegularExpressions.Regex pattern)
    {
        return pattern.IsMatch(fileName);
    }

    /// <summary>
    /// 格式化文件大小
    /// </summary>
    private static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

/// <summary>
/// 扫描到的文件信息
/// </summary>
public class ScannedFile
{
    public string Key { get; init; } = "";
    public long Size { get; init; }
    public DateTime LastModified { get; init; }
    public string MatchedPattern { get; init; } = "";
}

/// <summary>
/// 扫描统计信息
/// </summary>
public class ScanStatistics
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int PrefixesScanned { get; set; }
    public int FilesListed { get; set; }
    public int FilesMatched { get; set; }
    public long TotalSize { get; set; }
    public TimeSpan Elapsed => EndTime - StartTime;

    public override string ToString()
    {
        return $"扫描 {PrefixesScanned} 个目录，列出 {FilesListed} 个文件，匹配 {FilesMatched} 个文件，总大小 {FormatSize(TotalSize)}，耗时 {Elapsed.TotalSeconds:F1}s";
    }

    private static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}
