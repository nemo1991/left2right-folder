using System.IO;
using System.Text;
using System.Threading.Tasks;
using file_sync.Models;
using System.Linq;
using System.Collections.Generic;
using System;
namespace file_sync.Services;

/// <summary>
/// CSV 报告生成服务
/// </summary>
public class CsvReportGenerator : IReportGenerator
{
    public Task<bool> GenerateCsvAsync(MigrationReport report, string outputPath)
    {
        return Task.Run(() =>
        {
            var sb = new StringBuilder();
            var itemInfos = report.Left2Right.ItemInfos;

            // 写入统计摘要
            sb.AppendLine("===== 统计摘要 =====");
            sb.AppendLine($"开始时间：{report.StartTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"结束时间：{report.EndTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"耗时：{(report.EndTime - report.StartTime).TotalSeconds:F2} 秒");
            sb.AppendLine($"源目录：{report.SourceDirectory}");
            sb.AppendLine($"目标目录：{report.TargetDirectory}");
            sb.AppendLine($"总文件数：{itemInfos.Count}");
            sb.AppendLine($"扫描-待删除文件数：{itemInfos.Count(q => q.ScanResult == ScanResult.ToDelete)}");
            sb.AppendLine($"扫描-待迁移文件数：{itemInfos.Count(q => q.ScanResult == ScanResult.ToMove)}");
            sb.AppendLine($"扫描-冲突文件数：{itemInfos.Count(q => q.ScanResult == ScanResult.Confilct)}");
            sb.AppendLine($"扫描-异常文件数：{itemInfos.Count(q => q.ScanResult == ScanResult.Error)}");
            sb.AppendLine($"迁移-已删除文件数：{itemInfos.Count(q => q.MigrateResult == MigrateResult.Deleted)}");
            sb.AppendLine($"迁移-移动文件数：{itemInfos.Count(q => q.MigrateResult == MigrateResult.Moved)}");
            sb.AppendLine($"迁移-跳过文件数：{itemInfos.Count(q => q.MigrateResult == MigrateResult.Skipped)}");
            sb.AppendLine($"迁移-异常文件数：{itemInfos.Count(q => q.MigrateResult == MigrateResult.Fail)}");


            var header = new[]{
                "SourcePath",
                "ScanResult",
                "MigrateResult",
                "SFileSize",
                "SHash",
                "SCreatedTime",
                "SLastModified",
                "SLastAccessed",
                "TargetPath",
                "TFileSize",
                "THash",
                "TCreatedTime",
                "TLastModified",
                "TLastAccessed",
                "ScanMsg",
                "MigrateMsg"
            };


            sb.AppendLine(string.Join(",", header));

            // 写入详细记录
            foreach (var detail in report.Left2Right.ItemInfos)
            {
                var body = new[] {
                    detail.Source.FullPath,
                    detail.ScanResult.ToString(),
                    detail.MigrateResult?.ToString(),
                    FormatSize(detail.Source.FileSize),
                    detail.Source.Hash?.ToString(),
                    DateTimeToString(detail.Source.CreatedTime),
                    DateTimeToString(detail.Source.LastModified),
                    DateTimeToString(detail.Source.LastAccessTime),
                    detail.Target?.FullPath,
                    FormatSize(detail.Target?.FileSize),
                    detail.Target?.Hash?.ToString(),
                    DateTimeToString(detail.Target?.CreatedTime),
                    DateTimeToString(detail.Target?.LastModified),
                    DateTimeToString(detail.Target?.LastAccessTime),
                    detail.ScanMsg,
                    detail.MigrateMsg
                };

                sb.AppendLine(string.Join(",", body));
            }

            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
            return true;
        });


        static string DateTimeToString(DateTime? datetime)
        {
            if (datetime != null && datetime.HasValue)
            {

                return datetime.Value.ToString("yyyy-MM-dd HH:mm:ss");
            }

            return string.Empty;
        }

        static string FormatSize(long? bytes)
        {
            if (bytes == null || !bytes.HasValue || bytes.Value < 1) { return string.Empty; }
            double size = bytes.Value;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }
            return unitIndex == 0 ? $"{size:F0} {units[unitIndex]}" : $"{size:F2} {units[unitIndex]}";
        }
    }
}
