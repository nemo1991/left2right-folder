using file_sync.Models;
using file_sync.Services;

namespace file_sync.Tests;

public class FileComparatorTests : IDisposable
{
    private readonly string _sourceDir;
    private readonly string _targetDir;

    public FileComparatorTests()
    {
        _sourceDir = Path.Combine(Path.GetTempPath(), $"file-sync-src-{Guid.NewGuid():N}");
        _targetDir = Path.Combine(Path.GetTempPath(), $"file-sync-dst-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_targetDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_sourceDir)) Directory.Delete(_sourceDir, true);
        if (Directory.Exists(_targetDir)) Directory.Delete(_targetDir, true);
    }

    [Fact]
    public async Task CompareAsync_SameHash_MarksForDelete()
    {
        var content = "identical content";
        await File.WriteAllTextAsync(Path.Combine(_sourceDir, "file.txt"), content);
        await File.WriteAllTextAsync(Path.Combine(_targetDir, "file.txt"), content);

        var sourceFiles = new List<FileEntry>
        {
            new(Path.Combine(_sourceDir, "file.txt"), "file.txt", content.Length,
                DateTime.Now, DateTime.Now, DateTime.Now)
        };

        var comparator = new FileComparator();
        var hashCalc = new HashCalculator();
        var result = await comparator.CompareAsync(sourceFiles, _targetDir, hashCalc);

        Assert.Single(result.ToDelete);
        Assert.Empty(result.ToMove);
        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public async Task CompareAsync_DifferentHash_MarksAsConflict()
    {
        await File.WriteAllTextAsync(Path.Combine(_sourceDir, "file.txt"), "source content");
        await File.WriteAllTextAsync(Path.Combine(_targetDir, "file.txt"), "target content");

        var sourceFiles = new List<FileEntry>
        {
            new(Path.Combine(_sourceDir, "file.txt"), "file.txt", 14,
                DateTime.Now, DateTime.Now, DateTime.Now)
        };

        var comparator = new FileComparator();
        var hashCalc = new HashCalculator();
        var result = await comparator.CompareAsync(sourceFiles, _targetDir, hashCalc);

        Assert.Empty(result.ToDelete);
        Assert.Empty(result.ToMove);
        Assert.Single(result.Conflicts);
    }

    [Fact]
    public async Task CompareAsync_NoSameNameFile_MarksForMove()
    {
        await File.WriteAllTextAsync(Path.Combine(_sourceDir, "unique.txt"), "unique content");

        var sourceFiles = new List<FileEntry>
        {
            new(Path.Combine(_sourceDir, "unique.txt"), "unique.txt", 14,
                DateTime.Now, DateTime.Now, DateTime.Now)
        };

        var comparator = new FileComparator();
        var hashCalc = new HashCalculator();
        var result = await comparator.CompareAsync(sourceFiles, _targetDir, hashCalc);

        Assert.Empty(result.ToDelete);
        Assert.Single(result.ToMove);
        Assert.Empty(result.Conflicts);
    }

    [Fact]
    public async Task CompareAsync_MixedScenarios_CorrectClassification()
    {
        // Same content (delete)
        await File.WriteAllTextAsync(Path.Combine(_sourceDir, "same.txt"), "same");
        await File.WriteAllTextAsync(Path.Combine(_targetDir, "same.txt"), "same");

        // Different content (conflict)
        await File.WriteAllTextAsync(Path.Combine(_sourceDir, "diff.txt"), "src");
        await File.WriteAllTextAsync(Path.Combine(_targetDir, "diff.txt"), "dst");

        // No target file (move)
        await File.WriteAllTextAsync(Path.Combine(_sourceDir, "only_source.txt"), "move me");

        var sourceFiles = new List<FileEntry>
        {
            new(Path.Combine(_sourceDir, "same.txt"), "same.txt", 4, DateTime.Now, DateTime.Now, DateTime.Now),
            new(Path.Combine(_sourceDir, "diff.txt"), "diff.txt", 3, DateTime.Now, DateTime.Now, DateTime.Now),
            new(Path.Combine(_sourceDir, "only_source.txt"), "only_source.txt", 7, DateTime.Now, DateTime.Now, DateTime.Now),
        };

        var comparator = new FileComparator();
        var hashCalc = new HashCalculator();
        var result = await comparator.CompareAsync(sourceFiles, _targetDir, hashCalc);

        Assert.Single(result.ToDelete);
        Assert.Single(result.ToMove);
        Assert.Single(result.Conflicts);
    }

    [Fact]
    public async Task CompareAsync_EmptySourceFiles_ReturnsEmptyResult()
    {
        var sourceFiles = new List<FileEntry>();
        var comparator = new FileComparator();
        var hashCalc = new HashCalculator();
        var result = await comparator.CompareAsync(sourceFiles, _targetDir, hashCalc);

        Assert.Empty(result.ToDelete);
        Assert.Empty(result.ToMove);
        Assert.Empty(result.Conflicts);
        Assert.Equal(0, result.TotalSource);
    }

    [Fact]
    public async Task CompareAsync_DeletedFile_HasTargetInfo()
    {
        var content = "match";
        await File.WriteAllTextAsync(Path.Combine(_sourceDir, "match.txt"), content);
        await File.WriteAllTextAsync(Path.Combine(_targetDir, "match.txt"), content);

        var sourceFiles = new List<FileEntry>
        {
            new(Path.Combine(_sourceDir, "match.txt"), "match.txt", content.Length,
                DateTime.Now, DateTime.Now, DateTime.Now)
        };

        var comparator = new FileComparator();
        var hashCalc = new HashCalculator();
        var result = await comparator.CompareAsync(sourceFiles, _targetDir, hashCalc);

        var deleted = result.ToDelete[0];
        Assert.Equal("match.txt", deleted.SourceFile.FileName);
        Assert.Equal("match.txt", deleted.TargetFile.FileName);
        Assert.Equal(content.Length, deleted.TargetFile.FileSize);
        Assert.False(string.IsNullOrEmpty(deleted.SourceFile.Hash));
    }

    [Fact]
    public async Task CompareAsync_CancellationToken_Cancelled_ThrowsOperationCanceledException()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var sourceFiles = new List<FileEntry>();
        var comparator = new FileComparator();
        var hashCalc = new HashCalculator();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => comparator.CompareAsync(sourceFiles, _targetDir, hashCalc, null, cts.Token));
    }
}
