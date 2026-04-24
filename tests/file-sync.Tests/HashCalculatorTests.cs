using file_sync.Services;

namespace file_sync.Tests;

public class HashCalculatorTests : IDisposable
{
    private readonly string _testDir;

    public HashCalculatorTests()
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
    public async Task ComputeHashAsync_SameContent_ReturnsSameHash()
    {
        var file1 = Path.Combine(_testDir, "file1.txt");
        var file2 = Path.Combine(_testDir, "file2.txt");
        await File.WriteAllTextAsync(file1, "hello world");
        await File.WriteAllTextAsync(file2, "hello world");

        var calc = new HashCalculator();
        var hash1 = await calc.ComputeHashAsync(file1);
        var hash2 = await calc.ComputeHashAsync(file2);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public async Task ComputeHashAsync_DifferentContent_ReturnsDifferentHash()
    {
        var file1 = Path.Combine(_testDir, "file1.txt");
        var file2 = Path.Combine(_testDir, "file2.txt");
        await File.WriteAllTextAsync(file1, "content A");
        await File.WriteAllTextAsync(file2, "content B");

        var calc = new HashCalculator();
        var hash1 = await calc.ComputeHashAsync(file1);
        var hash2 = await calc.ComputeHashAsync(file2);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public async Task ComputeHashAsync_EmptyFile_ReturnsValidHash()
    {
        var file = Path.Combine(_testDir, "empty.txt");
        await File.WriteAllTextAsync(file, "");

        var calc = new HashCalculator();
        var hash = await calc.ComputeHashAsync(file);

        Assert.False(string.IsNullOrEmpty(hash));
        Assert.Equal(32, hash.Length); // MD5 = 32 hex chars
    }

    [Fact]
    public async Task ComputeHashAsync_BinaryFile_ReturnsValidHash()
    {
        var file = Path.Combine(_testDir, "binary.bin");
        var bytes = Enumerable.Range(0, 256).Select(b => (byte)b).ToArray();
        await File.WriteAllBytesAsync(file, bytes);

        var calc = new HashCalculator();
        var hash = await calc.ComputeHashAsync(file);

        Assert.False(string.IsNullOrEmpty(hash));
        Assert.Equal(32, hash.Length);
    }
}
