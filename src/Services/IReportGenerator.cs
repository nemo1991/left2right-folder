using System.Threading.Tasks;
using file_sync.Models;
namespace file_sync.Services;

/// <summary>
/// 报告生成服务接口
/// </summary>
public interface IReportGenerator
{
    Task<bool> GenerateCsvAsync(MigrationReport report, string outputPath);
}
