using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace iCode;

public class ToolExecutor
{
    private readonly string _workingDirectory;
    private readonly SkillsLoader? _skillsLoader;
    private readonly Func<string, Task<string>>? _subagentRunner;

    public ToolExecutor(string workingDirectory, SkillsLoader? skillsLoader = null, Func<string, Task<string>>? subagentRunner = null)
    {
        _workingDirectory = Path.GetFullPath(workingDirectory);
        _skillsLoader = skillsLoader;
        _subagentRunner = subagentRunner;
    }

    public async Task<string> ExecuteAsync(string toolName, string argumentsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var args = doc.RootElement;
            return toolName switch
            {
                "read_file"       => ReadFile(args),
                "write_file"      => WriteFile(args),
                "update_file"     => UpdateFile(args),
                "delete_file"     => DeleteFile(args),
                "list_files"      => ListFiles(args),
                "execute_command" => await ExecuteCommandAsync(args),
                "load_skill"      => LoadSkill(args),
                "run_subagent"    => await RunSubagentAsync(args),
                _                 => $"Error: unknown tool '{toolName}'"
            };
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private string ReadFile(JsonElement args)
    {
        var path = SafePath(args.GetProperty("path").GetString()!);
        return File.ReadAllText(path);
    }

    private string WriteFile(JsonElement args)
    {
        var path = SafePath(args.GetProperty("path").GetString()!);
        if (File.Exists(path))
            return $"Error: file already exists: {args.GetProperty("path").GetString()}";
        var dir = Path.GetDirectoryName(path);
        if (dir != null) Directory.CreateDirectory(dir);
        File.WriteAllText(path, args.GetProperty("content").GetString()!);
        return "File written successfully";
    }

    private string UpdateFile(JsonElement args)
    {
        var path    = SafePath(args.GetProperty("path").GetString()!);
        var oldText = args.GetProperty("old_text").GetString()!;
        var newText = args.GetProperty("new_text").GetString()!;

        var content = File.ReadAllText(path);
        var idx = content.IndexOf(oldText, StringComparison.Ordinal);
        if (idx < 0)
            return "Error: old_text not found in file";

        File.WriteAllText(path, content[..idx] + newText + content[(idx + oldText.Length)..]);
        return "File updated successfully";
    }

    private string DeleteFile(JsonElement args)
    {
        var path = SafePath(args.GetProperty("path").GetString()!);
        if (!File.Exists(path))
            return $"Error: file not found: {args.GetProperty("path").GetString()}";
        File.Delete(path);
        return "File deleted successfully";
    }

    private string ListFiles(JsonElement args)
    {
        string rel = "";
        if (args.TryGetProperty("path", out var pe))
            rel = pe.GetString() ?? "";

        var dir = string.IsNullOrWhiteSpace(rel) ? _workingDirectory : SafePath(rel);
        if (!Directory.Exists(dir))
            return $"Error: directory not found: {rel}";

        var entries = Directory.GetFileSystemEntries(dir)
            .Select(e => Path.GetRelativePath(_workingDirectory, e) + (Directory.Exists(e) ? "/" : ""))
            .OrderBy(e => e);
        return string.Join("\n", entries);
    }

    private async Task<string> ExecuteCommandAsync(JsonElement args)
    {
        var command = args.GetProperty("command").GetString()!;
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var psi = new ProcessStartInfo
        {
            FileName               = isWindows ? "cmd.exe" : "/bin/sh",
            WorkingDirectory       = _workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };
        psi.ArgumentList.Add(isWindows ? "/c" : "-c");
        psi.ArgumentList.Add(command);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start process");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var result = stdout;
        if (!string.IsNullOrEmpty(stderr))  result += $"\nSTDERR: {stderr}";
        if (process.ExitCode != 0)          result += $"\nExit code: {process.ExitCode}";
        return string.IsNullOrWhiteSpace(result) ? "(no output)" : result;
    }

    private async Task<string> RunSubagentAsync(JsonElement args)
    {
        if (_subagentRunner == null)
            return "Error: subagent is not available in this context";
        var task = args.GetProperty("task").GetString()!;
        return await _subagentRunner(task);
    }

    private string LoadSkill(JsonElement args)
    {
        if (_skillsLoader == null)
            return "Error: no skills available";
        var name = args.GetProperty("name").GetString()!;
        var body = _skillsLoader.GetBody(name);
        return body ?? $"Error: skill '{name}' not found";
    }

    public string SafePath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw new InvalidOperationException("Absolute paths are not allowed");

        var full = Path.GetFullPath(Path.Combine(_workingDirectory, relativePath));
        AssertWithin(full);

        // Resolve symlinks for existing paths
        FileSystemInfo? fsi = File.Exists(full)      ? new FileInfo(full)
                            : Directory.Exists(full) ? new DirectoryInfo(full)
                            : null;
        if (fsi != null)
        {
            var target = fsi.ResolveLinkTarget(returnFinalTarget: true);
            if (target != null) AssertWithin(target.FullName);
        }

        return full;
    }

    private void AssertWithin(string fullPath)
    {
        var normalized = fullPath.TrimEnd(Path.DirectorySeparatorChar);
        var workDir    = _workingDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (!normalized.Equals(workDir, StringComparison.Ordinal) &&
            !normalized.StartsWith(workDir + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Path escapes the working directory");
    }
}
