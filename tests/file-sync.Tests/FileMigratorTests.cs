using file_sync.Models;
using file_sync.Services;

namespace file_sync.Tests;

public class FileMigratorTests : IDisposable
{
    private readonly string _sourceDir;
    private readonly string _targetDir;

    public FileMigratorTests()
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

    private FileEntry CreateFileEntry(string fullPath, string fileName, string content)
    {
        return new FileEntry(fullPath, fileName, content.Length,
            DateTime.Now, DateTime.Now, DateTime.Now);
    }

    [Fact]
    public async Task MigrateAsync_Delete_RemovesSourceFile()
    {
        var sourceFile = Path.Combine(_sourceDir, "delete_me.txt");
        await File.WriteAllTextAsync(sourceFile, "content");
        var targetFile = Path.Combine(_targetDir, "delete_me.txt");
        await File.WriteAllTextAsync(targetFile, "content");

        var sourceEntry = CreateFileEntry(sourceFile, "delete_me.txt", "content");
        var targetEntry = new FileEntry(targetFile, "delete_me.txt", 7,
            DateTime.Now, DateTime.Now, DateTime.Now);
        var toDelete = new List<FileEntryToDelete>
        {
            new(sourceEntry, targetEntry)
        };

        var migrator = new FileMigrator();
        var result = await migrator.MigrateAsync(toDelete, [], [], _sourceDir, _targetDir);

        Assert.Equal(1, result.DeletedCount);
        Assert.False(File.Exists(sourceFile));
    }

    [Fact]
    public async Task MigrateAsync_Delete_NonExistentSource_Skips()
    {
        var sourceEntry = CreateFileEntry(Path.Combine(_sourceDir, "missing.txt"), "missing.txt", "");
        var targetEntry = CreateFileEntry(Path.Combine(_targetDir, "missing.txt"), "missing.txt", "");
        var toDelete = new List<FileEntryToDelete>
        {
            new(sourceEntry, targetEntry)
        };

        var migrator = new FileMigrator();
        var result = await migrator.MigrateAsync(toDelete, [], [], _sourceDir, _targetDir);

        Assert.Equal(0, result.DeletedCount);
        Assert.Equal(1, result.ConfilctCount);
    }

    [Fact]
    public async Task MigrateAsync_Move_CopiesFileToTarget()
    {
        var sourceFile = Path.Combine(_sourceDir, "move_me.txt");
        await File.WriteAllTextAsync(sourceFile, "move content");

        var toMove = new List<FileEntry>
        {
            CreateFileEntry(sourceFile, "move_me.txt", "move content")
        };

        var migrator = new FileMigrator();
        var result = await migrator.MigrateAsync([], toMove, [], _sourceDir, _targetDir);

        Assert.Equal(1, result.MigratedCount);
        Assert.False(File.Exists(sourceFile));
        Assert.True(File.Exists(Path.Combine(_targetDir, "move_me.txt")));
        var movedContent = await File.ReadAllTextAsync(Path.Combine(_targetDir, "move_me.txt"));
        Assert.Equal("move content", movedContent);
    }

    [Fact]
    public async Task MigrateAsync_Move_PreservesSubdirectoryStructure()
    {
        var subDir = Path.Combine(_sourceDir, "subdir");
        Directory.CreateDirectory(subDir);
        var sourceFile = Path.Combine(subDir, "nested.txt");
        await File.WriteAllTextAsync(sourceFile, "nested content");

        var toMove = new List<FileEntry>
        {
            CreateFileEntry(sourceFile, "nested.txt", "nested content")
        };

        var migrator = new FileMigrator();
        var result = await migrator.MigrateAsync([], toMove, [], _sourceDir, _targetDir);

        Assert.Equal(1, result.MigratedCount);
        Assert.True(File.Exists(Path.Combine(_targetDir, "subdir", "nested.txt")));
    }

    [Fact]
    public async Task MigrateAsync_Move_TargetAlreadyExists_RenamesWithSuffix()
    {
        var sourceFile = Path.Combine(_sourceDir, "duplicate.txt");
        await File.WriteAllTextAsync(sourceFile, "source version");
        var targetFile = Path.Combine(_targetDir, "duplicate.txt");
        await File.WriteAllTextAsync(targetFile, "target version");

        var toMove = new List<FileEntry>
        {
            CreateFileEntry(sourceFile, "duplicate.txt", "source version")
        };

        var migrator = new FileMigrator();
        var result = await migrator.MigrateAsync([], toMove, [], _sourceDir, _targetDir);

        Assert.Equal(1, result.MigratedCount);
        Assert.True(File.Exists(targetFile)); // Original target preserved
        Assert.True(File.Exists(Path.Combine(_targetDir, "duplicate_1.txt"))); // Renamed
    }

    [Fact]
    public async Task MigrateAsync_Conflicts_RecordedInDetails()
    {
        var conflicts = new List<FileEntry>
        {
            CreateFileEntry(Path.Combine(_sourceDir, "conflict.txt"), "conflict.txt", "conflict")
        };

        var migrator = new FileMigrator();
        var result = await migrator.MigrateAsync([], [], conflicts, _sourceDir, _targetDir);

        Assert.Equal(1, result.ConfilctCount);
        var conflictDetail = result.Details.First(d => d.Operation == "Conflict");
        Assert.Equal("Skipped", conflictDetail.Status);
        Assert.Contains("冲突", conflictDetail.ErrorMessage);
    }

    [Fact]
    public async Task MigrateAsync_MixedOperations_CorrectCounts()
    {
        // Create files for delete (same hash exists in target)
        await File.WriteAllTextAsync(Path.Combine(_sourceDir, "del.txt"), "del");
        await File.WriteAllTextAsync(Path.Combine(_targetDir, "del.txt"), "del");

        // Create files for move
        await File.WriteAllTextAsync(Path.Combine(_sourceDir, "move.txt"), "move");

        // Conflict file
        await File.WriteAllTextAsync(Path.Combine(_sourceDir, "conflict.txt"), "src");
        await File.WriteAllTextAsync(Path.Combine(_targetDir, "conflict.txt"), "dst");

        var toDelete = new List<FileEntryToDelete>
        {
            new(
                CreateFileEntry(Path.Combine(_sourceDir, "del.txt"), "del.txt", "del"),
                new FileEntry(Path.Combine(_targetDir, "del.txt"), "del.txt", 3,
                    DateTime.Now, DateTime.Now, DateTime.Now)
            )
        };
        var toMove = new List<FileEntry>
        {
            CreateFileEntry(Path.Combine(_sourceDir, "move.txt"), "move.txt", "move")
        };
        var conflicts = new List<FileEntry>
        {
            CreateFileEntry(Path.Combine(_sourceDir, "conflict.txt"), "conflict.txt", "src")
        };

        var migrator = new FileMigrator();
        var result = await migrator.MigrateAsync(toDelete, toMove, conflicts, _sourceDir, _targetDir);

        Assert.Equal(1, result.DeletedCount);
        Assert.Equal(1, result.MigratedCount);
        Assert.Equal(1, result.ConfilctCount); // 1 conflict, delete and move succeeded
        Assert.Equal(0, result.ErrorCount);
        Assert.Equal(3, result.Details.Count); // 1 delete + 1 move + 1 conflict
    }

    [Fact]
    public async Task MigrateAsync_CancellationToken_Cancelled_ThrowsOperationCanceledException()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var migrator = new FileMigrator();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => migrator.MigrateAsync([], [], [], _sourceDir, _targetDir, null, cts.Token));
    }

    [Fact]
    public async Task MigrateAsync_ReportsProgress()
    {
        await File.WriteAllTextAsync(Path.Combine(_sourceDir, "move.txt"), "move");

        var progressReported = new System.Threading.CountdownEvent(1);
        MigrationProgress? lastProgress = null;
        var progress = new Progress<MigrationProgress>(p =>
        {
            lastProgress = p;
            progressReported.Signal();
        });

        var toMove = new List<FileEntry>
        {
            CreateFileEntry(Path.Combine(_sourceDir, "move.txt"), "move.txt", "move")
        };

        var migrator = new FileMigrator();
        var result = await migrator.MigrateAsync([], toMove, [], _sourceDir, _targetDir, progress);

        // Wait for progress with timeout - some test environments don't fire Progress callbacks
        Assert.True(progressReported.Wait(500) || lastProgress != null,
            "Expected progress to be reported during migration");
        Assert.Equal(1, result.MigratedCount);
    }
}
