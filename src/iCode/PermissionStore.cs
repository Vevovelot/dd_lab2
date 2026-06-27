using System.Text.Json;

namespace iCode;

public class PermissionStore
{
    private readonly string _filePath;

    public PermissionStore(string workingDirectory)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var projectName = ProjectIdentity.GetProjectName(workingDirectory);
        _filePath = Path.Combine(home, ".iCode", projectName, "permissions");
    }

    public IReadOnlySet<string> LoadAlwaysGranted()
    {
        if (!File.Exists(_filePath))
            return new HashSet<string>();

        try
        {
            var json = File.ReadAllText(_filePath);
            var list = JsonSerializer.Deserialize<List<string>>(json);
            return new HashSet<string>(list ?? [], StringComparer.Ordinal);
        }
        catch
        {
            return new HashSet<string>();
        }
    }

    public void SaveAlwaysGranted(IReadOnlySet<string> tools)
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(tools.Order().ToList()));
    }
}
