using file_sync.Services;

namespace file_sync.Tests;

public class FileScannerTests : IDisposable
{
    private readonly string _testDir;

    public FileScannerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"file-sync-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Fact]
    public async Task ScanAsync_ReturnsAllFiles()
    {
        await File.WriteAllTextAsync(Path.Combine(_testDir, "a.txt"), "a");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "b.txt"), "b");
        await File.WriteAllTextAsync(Path.Combine(_testDir, "c.txt"), "c");

        var scanner = new FileScanner();
        var files = await scanner.ScanAsync(_testDir);

        Assert.Equal(3, files.Count);
    }

    [Fact]
    public async Task ScanAsync_IncludesSubdirectoryFiles()
    {
        var subDir = Path.Combine(_testDir, "sub");
        Directory.CreateDirectory(subDir);
        await File.WriteAllTextAsync(Path.Combine(_testDir, "root.txt"), "root");
        await File.WriteAllTextAsync(Path.Combine(subDir, "child.txt"), "child");

        var scanner = new FileScanner();
        var files = await scanner.ScanAsync(_testDir);

        Assert.Equal(2, files.Count);
    }

    [Fact]
    public async Task ScanAsync_EmptyDirectory_ReturnsEmptyList()
    {
        var scanner = new FileScanner();
        var files = await scanner.ScanAsync(_testDir);

        Assert.Empty(files);
    }

    [Fact]
    public async Task ScanAsync_NonExistentDirectory_ThrowsDirectoryNotFoundException()
    {
        var scanner = new FileScanner();
        var ex = await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => scanner.ScanAsync(Path.Combine(Path.GetTempPath(), "non-existent-dir")));
        Assert.Contains("目录不存在", ex.Message);
    }

    [Fact]
    public async Task ScanAsync_FileEntry_ContainsCorrectFileName()
    {
        await File.WriteAllTextAsync(Path.Combine(_testDir, "test.txt"), "content");

        var scanner = new FileScanner();
        var files = await scanner.ScanAsync(_testDir);

        var file = Assert.Single(files);
        Assert.Equal("test.txt", file.FileName);
        Assert.Equal(7, file.FileSize);
    }

    [Fact]
    public async Task ScanAsync_CancellationToken_Cancelled_ThrowsOperationCanceledException()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var scanner = new FileScanner();
        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scanner.ScanAsync(_testDir, null, cts.Token));
    }

    [Fact]
    public async Task ScanAsync_ReportsProgress()
    {
        var reportedPaths = new List<string>();
        var progress = new Progress<string>(p => reportedPaths.Add(p));

        for (int i = 0; i < 150; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(_testDir, $"file{i:D3}.txt"), $"content{i}");
        }

        var scanner = new FileScanner();
        await scanner.ScanAsync(_testDir, progress);

        // 150 files / 100 interval + final report = 2 progress reports
        Assert.Equal(2, reportedPaths.Count);
    }
}
