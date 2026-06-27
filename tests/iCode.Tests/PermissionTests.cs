namespace iCode.Tests;

public class PermissionStoreTests : IDisposable
{
    private readonly string _workDir;
    private readonly PermissionStore _store;

    public PermissionStoreTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "perm_store_test_" + Guid.NewGuid());
        Directory.CreateDirectory(_workDir);
        _store = new PermissionStore(_workDir);
    }

    public void Dispose() => Directory.Delete(_workDir, recursive: true);

    [Fact]
    public void LoadAlwaysGranted_ReturnsEmpty_WhenFileAbsent()
    {
        var result = _store.LoadAlwaysGranted();
        Assert.Empty(result);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips_ToolNames()
    {
        var tools = new HashSet<string> { "write_file", "execute_command" };
        _store.SaveAlwaysGranted(tools);
        var loaded = _store.LoadAlwaysGranted();
        Assert.Contains("write_file", loaded);
        Assert.Contains("execute_command", loaded);
    }

    [Fact]
    public void LoadAlwaysGranted_ReturnsEmpty_WhenFileCorrupted()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var projectName = ProjectIdentity.GetProjectName(_workDir);
        var path = Path.Combine(home, ".iCode", projectName, "permissions");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not valid json{{");

        var result = _store.LoadAlwaysGranted();
        Assert.Empty(result);
    }
}

public class PermissionManagerTests
{
    private static PermissionStore MakeStore()
    {
        var workDir = Path.Combine(Path.GetTempPath(), "perm_mgr_test_" + Guid.NewGuid());
        Directory.CreateDirectory(workDir);
        return new PermissionStore(workDir);
    }

    [Fact]
    public async Task RequestAsync_ReturnsFalse_WhenDenied()
    {
        var mgr = new PermissionManager(MakeStore(), (_, _) => Task.FromResult<PermissionGrant?>(null));
        Assert.False(await mgr.RequestAsync("write_file", "Write file: foo.txt"));
    }

    [Fact]
    public async Task RequestAsync_ReturnsTrue_ForOnce_ThenRequiresAgain()
    {
        var callCount = 0;
        var mgr = new PermissionManager(MakeStore(), (_, _) =>
        {
            callCount++;
            return Task.FromResult<PermissionGrant?>(PermissionGrant.Once);
        });

        Assert.True(await mgr.RequestAsync("write_file", "Write: a.txt"));
        Assert.True(await mgr.RequestAsync("write_file", "Write: b.txt"));
        Assert.Equal(2, callCount); // asked twice — Once does not cache
    }

    [Fact]
    public async Task RequestAsync_SessionGrant_DoesNotAskAgain()
    {
        var callCount = 0;
        var mgr = new PermissionManager(MakeStore(), (_, _) =>
        {
            callCount++;
            return Task.FromResult<PermissionGrant?>(PermissionGrant.Session);
        });

        Assert.True(await mgr.RequestAsync("execute_command", "ls"));
        Assert.True(await mgr.RequestAsync("execute_command", "pwd"));
        Assert.Equal(1, callCount); // asked only once
    }

    [Fact]
    public async Task RequestAsync_AlwaysGrant_PersistsToStore()
    {
        var store = MakeStore();
        var mgr = new PermissionManager(store, (_, _) =>
            Task.FromResult<PermissionGrant?>(PermissionGrant.Always));

        Assert.True(await mgr.RequestAsync("delete_file", "Delete: x.txt"));
        var saved = store.LoadAlwaysGranted();
        Assert.Contains("delete_file", saved);
    }

    [Fact]
    public async Task RequestAsync_LoadsAlwaysGrantedFromStore_WithoutAsking()
    {
        var store = MakeStore();
        store.SaveAlwaysGranted(new HashSet<string> { "write_file" });

        var callCount = 0;
        var mgr = new PermissionManager(store, (_, _) =>
        {
            callCount++;
            return Task.FromResult<PermissionGrant?>(null);
        });

        Assert.True(await mgr.RequestAsync("write_file", "Write: z.txt"));
        Assert.Equal(0, callCount); // never asked
    }

    [Fact]
    public async Task RequestAsync_NonInteractive_ReturnsFalse_WhenNoGrant()
    {
        var mgr = new PermissionManager(MakeStore(), requestPermission: null);
        Assert.False(await mgr.RequestAsync("execute_command", "rm -rf /"));
    }
}

public class ToolExecutorPermissionTests : IDisposable
{
    private readonly string _workDir;

    public ToolExecutorPermissionTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "tool_perm_test_" + Guid.NewGuid());
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose() => Directory.Delete(_workDir, recursive: true);

    private PermissionManager DenyAll() =>
        new(new PermissionStore(_workDir), (_, _) => Task.FromResult<PermissionGrant?>(null));

    private PermissionManager AllowAll() =>
        new(new PermissionStore(_workDir), (_, _) => Task.FromResult<PermissionGrant?>(PermissionGrant.Once));

    [Fact]
    public async Task WriteFile_Denied_ReturnsPermissionError()
    {
        var executor = new ToolExecutor(_workDir, permissions: DenyAll());
        var result = await executor.ExecuteAsync("write_file", """{"path":"test.txt","content":"hi"}""");
        Assert.Contains("Permission denied", result);
        Assert.False(File.Exists(Path.Combine(_workDir, "test.txt")));
    }

    [Fact]
    public async Task WriteFile_Allowed_CreatesFile()
    {
        var executor = new ToolExecutor(_workDir, permissions: AllowAll());
        var result = await executor.ExecuteAsync("write_file", """{"path":"test.txt","content":"hi"}""");
        Assert.Equal("File written successfully", result);
        Assert.True(File.Exists(Path.Combine(_workDir, "test.txt")));
    }

    [Fact]
    public async Task DeleteFile_Denied_ReturnsPermissionError()
    {
        File.WriteAllText(Path.Combine(_workDir, "del.txt"), "x");
        var executor = new ToolExecutor(_workDir, permissions: DenyAll());
        var result = await executor.ExecuteAsync("delete_file", """{"path":"del.txt"}""");
        Assert.Contains("Permission denied", result);
        Assert.True(File.Exists(Path.Combine(_workDir, "del.txt")));
    }

    [Fact]
    public async Task ExecuteCommand_Denied_ReturnsPermissionError()
    {
        var executor = new ToolExecutor(_workDir, permissions: DenyAll());
        var result = await executor.ExecuteAsync("execute_command", """{"command":"echo hi"}""");
        Assert.Contains("Permission denied", result);
    }

    [Fact]
    public async Task ReadFile_NoPermissionCheck()
    {
        File.WriteAllText(Path.Combine(_workDir, "r.txt"), "hello");
        var executor = new ToolExecutor(_workDir, permissions: DenyAll());
        var result = await executor.ExecuteAsync("read_file", """{"path":"r.txt"}""");
        Assert.Equal("hello", result);
    }
}
