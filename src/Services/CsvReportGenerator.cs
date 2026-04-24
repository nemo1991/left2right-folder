using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using file_sync.Models;

namespace file_sync.Services;

/// <summary>
/// 报告生成服务接口
/// </summary>
public interface IReportGenerator
{
    Task<string> GenerateCsvAsync(MigrationReport report, string outputPath);
}

/// <summary>
/// CSV 报告生成服务
/// </summary>
public class CsvReportGenerator : IReportGenerator
{
    public Task<string> GenerateCsvAsync(MigrationReport report, string outputPath)
    {
        return Task.Run(() =>
        {
            var sb = new StringBuilder();

            // 写入表头（使用半角逗号分隔）
            sb.AppendLine("Operation,SourcePath,TargetPath,FileSize,CreatedTime,LastModified,LastAccessed,Hash,Status,ErrorMessage");

            // 写入详细记录
            foreach (var detail in report.Details)
            {
                sb.AppendLine($"{EscapeCsv(detail.Operation)},{EscapeCsv(detail.SourcePath)},{EscapeCsv(detail.TargetPath)},{detail.FileSize},{detail.CreatedTime:yyyy-MM-dd HH:mm:ss},{detail.LastModified:yyyy-MM-dd HH:mm:ss},{detail.LastAccessed:yyyy-MM-dd HH:mm:ss},{EscapeCsv(detail.Hash)},{EscapeCsv(detail.Status)},{EscapeCsv(detail.ErrorMessage)}");
            }

            // 写入统计摘要
            sb.AppendLine();
            sb.AppendLine("===== 统计摘要 =====");
            sb.AppendLine($"开始时间：{report.StartTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"结束时间：{report.EndTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"耗时：{(report.EndTime - report.StartTime).TotalSeconds:F2} 秒");
            sb.AppendLine($"源目录：{report.SourceDirectory}");
            sb.AppendLine($"目标目录：{report.TargetDirectory}");
            sb.AppendLine($"总扫描文件数：{report.TotalScanned}");
            sb.AppendLine($"删除文件数：{report.DeletedCount}");
            sb.AppendLine($"迁移文件数：{report.MigratedCount}");
            sb.AppendLine($"跳过文件数：{report.SkippedCount}");
            sb.AppendLine($"错误数：{report.ErrorCount}");

            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
            return outputPath;
        });
    }

    private string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        // CSV 转义：包含逗号、引号、换行的字段用引号包裹，引号 doubled
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }
}
