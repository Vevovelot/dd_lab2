using iCode;

namespace iCode.Tests;

public class ToolExecutorTests : IDisposable
{
    private readonly string _workDir;
    private readonly ToolExecutor _executor;

    public ToolExecutorTests()
    {
        _workDir  = Path.Combine(Path.GetTempPath(), "icode_tool_test_" + Guid.NewGuid());
        Directory.CreateDirectory(_workDir);
        _executor = new ToolExecutor(_workDir);
    }

    // --- SafePath security ---

    [Fact]
    public void SafePath_DotDot_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _executor.SafePath("../escape"));
    }

    [Fact]
    public void SafePath_AbsolutePath_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _executor.SafePath("/etc/passwd"));
    }

    [Fact]
    public void SafePath_NestedDotDot_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _executor.SafePath("sub/../../escape"));
    }

    [Fact]
    public void SafePath_ValidRelative_ReturnsFullPath()
    {
        var result = _executor.SafePath("sub/file.txt");
        Assert.StartsWith(_workDir, result);
        Assert.EndsWith("file.txt", result);
    }

    [Fact]
    public void SafePath_SymlinkOutside_Throws()
    {
        var outside = Path.GetTempPath();
        var link    = Path.Combine(_workDir, "evil_link");
        try
        {
            Directory.CreateSymbolicLink(link, outside);
            Assert.Throws<InvalidOperationException>(() => _executor.SafePath("evil_link"));
        }
        catch (UnauthorizedAccessException)
        {
            // Symlinks not supported in this environment — skip
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
        }
    }

    // --- read_file ---

    [Fact]
    public async Task ReadFile_ReturnsContent()
    {
        File.WriteAllText(Path.Combine(_workDir, "hello.txt"), "hello world");
        var result = await _executor.ExecuteAsync("read_file", """{"path":"hello.txt"}""");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public async Task ReadFile_MissingFile_ReturnsError()
    {
        var result = await _executor.ExecuteAsync("read_file", """{"path":"missing.txt"}""");
        Assert.StartsWith("Error:", result);
    }

    // --- write_file ---

    [Fact]
    public async Task WriteFile_CreatesFile()
    {
        var result = await _executor.ExecuteAsync("write_file", """{"path":"new.txt","content":"abc"}""");
        Assert.Equal("File written successfully", result);
        Assert.Equal("abc", File.ReadAllText(Path.Combine(_workDir, "new.txt")));
    }

    [Fact]
    public async Task WriteFile_ExistingFile_ReturnsError()
    {
        File.WriteAllText(Path.Combine(_workDir, "exists.txt"), "x");
        var result = await _executor.ExecuteAsync("write_file", """{"path":"exists.txt","content":"y"}""");
        Assert.StartsWith("Error:", result);
    }

    [Fact]
    public async Task WriteFile_EscapePath_ReturnsError()
    {
        var result = await _executor.ExecuteAsync("write_file", """{"path":"../outside.txt","content":"x"}""");
        Assert.StartsWith("Error:", result);
    }

    // --- update_file ---

    [Fact]
    public async Task UpdateFile_ReplacesFirstOccurrence()
    {
        File.WriteAllText(Path.Combine(_workDir, "u.txt"), "aaa bbb aaa");
        var result = await _executor.ExecuteAsync("update_file",
            """{"path":"u.txt","old_text":"aaa","new_text":"ccc"}""");
        Assert.Equal("File updated successfully", result);
        Assert.Equal("ccc bbb aaa", File.ReadAllText(Path.Combine(_workDir, "u.txt")));
    }

    [Fact]
    public async Task UpdateFile_OldTextNotFound_ReturnsError()
    {
        File.WriteAllText(Path.Combine(_workDir, "u2.txt"), "hello");
        var result = await _executor.ExecuteAsync("update_file",
            """{"path":"u2.txt","old_text":"xyz","new_text":"abc"}""");
        Assert.StartsWith("Error:", result);
    }

    // --- delete_file ---

    [Fact]
    public async Task DeleteFile_DeletesFile()
    {
        var path = Path.Combine(_workDir, "del.txt");
        File.WriteAllText(path, "x");
        var result = await _executor.ExecuteAsync("delete_file", """{"path":"del.txt"}""");
        Assert.Equal("File deleted successfully", result);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task DeleteFile_MissingFile_ReturnsError()
    {
        var result = await _executor.ExecuteAsync("delete_file", """{"path":"ghost.txt"}""");
        Assert.StartsWith("Error:", result);
    }

    // --- list_files ---

    [Fact]
    public async Task ListFiles_ListsRootEntries()
    {
        File.WriteAllText(Path.Combine(_workDir, "a.txt"), "");
        File.WriteAllText(Path.Combine(_workDir, "b.txt"), "");
        var result = await _executor.ExecuteAsync("list_files", """{}""");
        Assert.Contains("a.txt", result);
        Assert.Contains("b.txt", result);
    }

    [Fact]
    public async Task ListFiles_Subdirectory_Works()
    {
        var sub = Path.Combine(_workDir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "c.txt"), "");
        var result = await _executor.ExecuteAsync("list_files", """{"path":"sub"}""");
        Assert.Contains("c.txt", result);
    }

    // --- execute_command ---

    [Fact]
    public async Task ExecuteCommand_ReturnsOutput()
    {
        var result = await _executor.ExecuteAsync("execute_command", """{"command":"echo hello"}""");
        Assert.Contains("hello", result);
    }

    [Fact]
    public async Task ExecuteCommand_WorksInWorkingDirectory()
    {
        File.WriteAllText(Path.Combine(_workDir, "marker.txt"), "");
        var result = await _executor.ExecuteAsync("execute_command", """{"command":"ls marker.txt"}""");
        Assert.Contains("marker.txt", result);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workDir))
            Directory.Delete(_workDir, recursive: true);
    }
}
