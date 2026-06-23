using System.Security.Cryptography;
using System.Text;

namespace iCode;

public static class ProjectIdentity
{
    public static string GetProjectName(string workingDirectory)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(workingDirectory));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string GetContextDbPath(string workingDirectory)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var projectName = GetProjectName(workingDirectory);
        return Path.Combine(home, ".iCode", projectName, "context");
    }
}
