using file_sync.ViewModels;

namespace file_sync.Tests;

public class ObjectStorageSyncViewModelTests : IDisposable
{
    private readonly string _testDir;

    public ObjectStorageSyncViewModelTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"obj-sync-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.00 KB")]
    [InlineData(1536, "1.50 KB")]
    [InlineData(1048576, "1.00 MB")]
    [InlineData(1572864, "1.50 MB")]
    [InlineData(1073741824, "1.00 GB")]
    [InlineData(1099511627776, "1.00 TB")]
    public void FormatSize_ReturnsHumanReadableSize(long bytes, string expected)
    {
        // Use reflection to call the private static method
        var method = typeof(ObjectStorageSyncViewModel)
            .GetMethod("FormatSize", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var result = (string)method.Invoke(null, new object[] { bytes });
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("simple", "simple")]
    [InlineData("with,comma", "\"with,comma\"")]
    [InlineData("with\"quote", "\"with\"\"quote\"")]
    [InlineData("with\nnewline", "\"with\nnewline\"")]
    [InlineData("with\rreturn", "\"with\rreturn\"")]
    public void EscapeCsv_ReturnsEscapedString(string input, string expected)
    {
        var method = typeof(ObjectStorageSyncViewModel)
            .GetMethod("EscapeCsv", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var result = (string)method.Invoke(null, new object[] { input });
        Assert.Equal(expected, result);
    }

    [Fact]
    public void BuildObjectKey_NoPrefix_ReturnsRelativePath()
    {
        var vm = new ObjectStorageSyncViewModel();
        vm.LocalDirectory = _testDir;

        var subDir = Path.Combine(_testDir, "subdir");
        Directory.CreateDirectory(subDir);
        var filePath = Path.Combine(subDir, "test.txt");

        var method = typeof(ObjectStorageSyncViewModel)
            .GetMethod("BuildObjectKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);

        var result = (string)method.Invoke(vm, new object[] { filePath });
        Assert.Equal("subdir/test.txt", result);
    }

    [Fact]
    public void BuildObjectKey_WithPrefix_AddsPrefix()
    {
        var vm = new ObjectStorageSyncViewModel();
        vm.LocalDirectory = _testDir;
        vm.Prefix = "backup";

        var filePath = Path.Combine(_testDir, "test.txt");

        var method = typeof(ObjectStorageSyncViewModel)
            .GetMethod("BuildObjectKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);

        var result = (string)method.Invoke(vm, new object[] { filePath });
        Assert.Equal("backup/test.txt", result);
    }

    [Fact]
    public void BuildObjectKey_PrefixWithSlash_NormalizesPath()
    {
        var vm = new ObjectStorageSyncViewModel();
        vm.LocalDirectory = _testDir;
        vm.Prefix = "backup/folder";

        var filePath = Path.Combine(_testDir, "test.txt");

        var method = typeof(ObjectStorageSyncViewModel)
            .GetMethod("BuildObjectKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);

        var result = (string)method.Invoke(vm, new object[] { filePath });
        Assert.Equal("backup/folder/test.txt", result);
    }

    [Fact]
    public void BuildObjectKey_UsesForwardSlash()
    {
        var vm = new ObjectStorageSyncViewModel();
        vm.LocalDirectory = _testDir;
        vm.Prefix = "";

        var subDir = Path.Combine(_testDir, "sub", "nested");
        Directory.CreateDirectory(subDir);
        var filePath = Path.Combine(subDir, "test.txt");

        var method = typeof(ObjectStorageSyncViewModel)
            .GetMethod("BuildObjectKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);

        var result = (string)method.Invoke(vm, new object[] { filePath });
        Assert.Contains("/", result);
        Assert.DoesNotContain("\\", result);
    }

    [Fact]
    public void UpdateCanScan_AllFieldsEmpty_ReturnsFalse()
    {
        var vm = new ObjectStorageSyncViewModel();
        vm.LocalDirectory = "";
        vm.Endpoint = "";
        vm.AccessKey = "";
        vm.SecretKey = "";
        vm.Bucket = "";

        Assert.False(vm.CanScan);
    }

    [Fact]
    public void UpdateCanScan_AllFieldsFilled_ReturnsTrue()
    {
        var vm = new ObjectStorageSyncViewModel();
        vm.LocalDirectory = "C:\\test";
        vm.Endpoint = "s3.amazonaws.com";
        vm.AccessKey = "access";
        vm.SecretKey = "secret";
        vm.Bucket = "my-bucket";

        Assert.True(vm.CanScan);
    }

    [Fact]
    public void UpdateCanScan_MissingLocalDirectory_ReturnsFalse()
    {
        var vm = new ObjectStorageSyncViewModel();
        vm.LocalDirectory = "";
        vm.Endpoint = "s3.amazonaws.com";
        vm.AccessKey = "access";
        vm.SecretKey = "secret";
        vm.Bucket = "my-bucket";

        Assert.False(vm.CanScan);
    }

    [Fact]
    public void UpdateCanScan_MissingEndpoint_ReturnsFalse()
    {
        var vm = new ObjectStorageSyncViewModel();
        vm.LocalDirectory = "C:\\test";
        vm.Endpoint = "";
        vm.AccessKey = "access";
        vm.SecretKey = "secret";
        vm.Bucket = "my-bucket";

        Assert.False(vm.CanScan);
    }

    [Fact]
    public void ResetState_ClearsCountAndSize()
    {
        var vm = new ObjectStorageSyncViewModel();

        // Set some state via reflection or by calling internal methods
        var resetMethod = typeof(ObjectStorageSyncViewModel)
            .GetMethod("ResetState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(resetMethod);

        resetMethod.Invoke(vm, null);

        Assert.Equal(0, vm.ToSyncCount);
        Assert.Equal("", vm.TotalSizeText);
    }

    [Fact]
    public void ObjectStorageSyncViewModel_InitialState()
    {
        var vm = new ObjectStorageSyncViewModel();

        Assert.Equal("", vm.LocalDirectory);
        Assert.Equal("", vm.Endpoint);
        Assert.Equal("", vm.AccessKey);
        Assert.Equal("", vm.SecretKey);
        Assert.Equal("", vm.Bucket);
        Assert.Equal("", vm.Prefix);
        Assert.Equal(0, vm.StorageType);
        Assert.Equal(1, vm.SyncMode);
        Assert.Equal("就绪", vm.StatusMessage);
        Assert.Equal(0, vm.ProgressValue);
        Assert.False(vm.IsProgressIndeterminate);
        Assert.False(vm.CanScan);
        Assert.False(vm.CanSync);
        Assert.False(vm.CanCancel);
        Assert.Equal("扫描", vm.ScanButtonContent);
        Assert.Equal("开始同步", vm.SyncButtonContent);
        Assert.Equal(0, vm.ToSyncCount);
        Assert.NotNull(vm.Logs);
    }

    [Fact]
    public void OnLocalDirectoryChanged_UpdatesCanScanAndResetsState()
    {
        var vm = new ObjectStorageSyncViewModel();

        // First fill all required fields
        vm.LocalDirectory = "C:\\test";
        vm.Endpoint = "s3.amazonaws.com";
        vm.AccessKey = "access";
        vm.SecretKey = "secret";
        vm.Bucket = "bucket";

        Assert.True(vm.CanScan);

        // Now change local directory - should reset state
        vm.LocalDirectory = "C:\\other";

        Assert.Equal(0, vm.ToSyncCount);
    }

    [Fact]
    public void OnEndpointChanged_UpdatesCanScan()
    {
        var vm = new ObjectStorageSyncViewModel();
        vm.LocalDirectory = "C:\\test";
        vm.AccessKey = "access";
        vm.SecretKey = "secret";
        vm.Bucket = "bucket";

        Assert.False(vm.CanScan);

        vm.Endpoint = "s3.amazonaws.com";

        Assert.True(vm.CanScan);
    }

    [Fact]
    public void OnAccessKeyChanged_UpdatesCanScan()
    {
        var vm = new ObjectStorageSyncViewModel();
        vm.LocalDirectory = "C:\\test";
        vm.Endpoint = "s3.amazonaws.com";
        vm.SecretKey = "secret";
        vm.Bucket = "bucket";

        Assert.False(vm.CanScan);

        vm.AccessKey = "access";

        Assert.True(vm.CanScan);
    }

    [Fact]
    public void OnSecretKeyChanged_UpdatesCanScan()
    {
        var vm = new ObjectStorageSyncViewModel();
        vm.LocalDirectory = "C:\\test";
        vm.Endpoint = "s3.amazonaws.com";
        vm.AccessKey = "access";
        vm.Bucket = "bucket";

        Assert.False(vm.CanScan);

        vm.SecretKey = "secret";

        Assert.True(vm.CanScan);
    }

    [Fact]
    public void OnBucketChanged_UpdatesCanScan()
    {
        var vm = new ObjectStorageSyncViewModel();
        vm.LocalDirectory = "C:\\test";
        vm.Endpoint = "s3.amazonaws.com";
        vm.AccessKey = "access";
        vm.SecretKey = "secret";

        Assert.False(vm.CanScan);

        vm.Bucket = "bucket";

        Assert.True(vm.CanScan);
    }

    [Fact]
    public void Cancel_SetsStatusMessage()
    {
        var vm = new ObjectStorageSyncViewModel();
        vm.Cancel();

        Assert.Equal("正在取消...", vm.StatusMessage);
    }
}
