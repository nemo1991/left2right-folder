using file_sync.Models;
using file_sync.Services;

namespace file_sync.Tests;

public class CsvReportGeneratorTests : IDisposable
{
    private readonly string _outputDir;

    public CsvReportGeneratorTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), $"file-sync-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outputDir)) Directory.Delete(_outputDir, true);
    }

    private MigrationReport CreateReport(params MigrationDetail[] details)
    {
        return new MigrationReport(
            DateTime.Now.AddSeconds(-10),
            DateTime.Now,
            @"C:\source",
            @"C:\target",
            100,
            50,
            30,
            10,
            5,
            5,
            details.ToList()
        );
    }

    [Fact]
    public async Task GenerateCsvAsync_GeneratesFile()
    {
        var outputPath = Path.Combine(_outputDir, "report.csv");
        var report = CreateReport();

        var generator = new CsvReportGenerator();
        var path = await generator.GenerateCsvAsync(report, outputPath);

        Assert.Equal(outputPath, path);
        Assert.True(File.Exists(outputPath));
    }

    [Fact]
    public async Task GenerateCsvAsync_ContainsHeaders()
    {
        var outputPath = Path.Combine(_outputDir, "report.csv");
        var report = CreateReport();

        var generator = new CsvReportGenerator();
        await generator.GenerateCsvAsync(report, outputPath);

        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("Operation,SourcePath,TargetPath,FileSize", content);
        Assert.Contains("TargetFileSize,TargetFileHash,TargetFileCreatedTime", content);
    }

    [Fact]
    public async Task GenerateCsvAsync_ContainsDetailRows()
    {
        var outputPath = Path.Combine(_outputDir, "report.csv");
        var details = new[]
        {
            new MigrationDetail("Delete", @"C:\source\a.txt", @"C:\target\a.txt",
                100, "abc123",
                DateTime.Now, DateTime.Now, DateTime.Now,
                "Success", "",
                100, "abc123", DateTime.Now, DateTime.Now, DateTime.Now),
            new MigrationDetail("Move", @"C:\source\b.txt", @"C:\target\b.txt",
                200, "def456",
                DateTime.Now, DateTime.Now, DateTime.Now,
                "Success", ""),
        };
        var report = CreateReport(details);

        var generator = new CsvReportGenerator();
        await generator.GenerateCsvAsync(report, outputPath);

        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("Delete", content);
        Assert.Contains("Move", content);
        Assert.Contains("abc123", content);
        Assert.Contains("def456", content);
    }

    [Fact]
    public async Task GenerateCsvAsync_ContainsSummary()
    {
        var outputPath = Path.Combine(_outputDir, "report.csv");
        var report = CreateReport();

        var generator = new CsvReportGenerator();
        await generator.GenerateCsvAsync(report, outputPath);

        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("===== 统计摘要 =====", content);
        Assert.Contains("总扫描文件数：100", content);
        Assert.Contains("删除文件数：50", content);
        Assert.Contains("迁移文件数：30", content);
        Assert.Contains("冲突文件数：5", content);
        Assert.Contains("跳过文件数：10", content);
        Assert.Contains("错误数：5", content);
    }

    [Fact]
    public async Task GenerateCsvAsync_EscapesCommasInFields()
    {
        var outputPath = Path.Combine(_outputDir, "report.csv");
        var detail = new MigrationDetail(
            "Move", @"C:\source\file.txt", @"C:\target\file.txt",
            0, "",
            DateTime.Now, DateTime.Now, DateTime.Now,
            "Failed", "Error, something went wrong");
        var report = CreateReport(detail);

        var generator = new CsvReportGenerator();
        await generator.GenerateCsvAsync(report, outputPath);

        var content = await File.ReadAllTextAsync(outputPath);
        // The error message with comma should be quoted
        Assert.Contains("\"Error, something went wrong\"", content);
    }

    [Fact]
    public async Task GenerateCsvAsync_EscapesQuotesInFields()
    {
        var outputPath = Path.Combine(_outputDir, "report.csv");
        var detail = new MigrationDetail(
            "Move", @"C:\source\file.txt", @"C:\target\file.txt",
            0, "",
            DateTime.Now, DateTime.Now, DateTime.Now,
            "Failed", "Error: \"file not found\"");
        var report = CreateReport(detail);

        var generator = new CsvReportGenerator();
        await generator.GenerateCsvAsync(report, outputPath);

        var content = await File.ReadAllTextAsync(outputPath);
        Assert.Contains("\"\"file not found\"\"", content); // quotes should be doubled
    }

    [Fact]
    public async Task GenerateCsvAsync_UsesCorrectEncoding()
    {
        var outputPath = Path.Combine(_outputDir, "report.csv");
        var report = CreateReport();

        var generator = new CsvReportGenerator();
        await generator.GenerateCsvAsync(report, outputPath);

        var bytes = File.ReadAllBytes(outputPath);
        // UTF-8 BOM: EF BB BF
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
    }

    [Fact]
    public async Task GenerateCsvAsync_EmptyDetails_GeneratesValidReport()
    {
        var outputPath = Path.Combine(_outputDir, "report.csv");
        var report = CreateReport();

        var generator = new CsvReportGenerator();
        await generator.GenerateCsvAsync(report, outputPath);

        var content = await File.ReadAllTextAsync(outputPath);
        var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("Operation,SourcePath", lines[0]);
        Assert.Contains("===== 统计摘要 =====", lines[1]);
    }
}
