using System.Text;

namespace iCode;

public record SkillInfo(string Name, string Description, string FilePath);

public class SkillsLoader
{
    private readonly IReadOnlyList<SkillInfo> _skills;

    public SkillsLoader(string workingDirectory)
    {
        var skillsDir = Path.Combine(workingDirectory, "SKILLS");
        if (!Directory.Exists(skillsDir))
        {
            _skills = [];
            return;
        }

        _skills = Directory.GetFiles(skillsDir, "*.md")
            .Select(TryParseHeader)
            .Where(s => s != null)
            .Cast<SkillInfo>()
            .OrderBy(s => s.Name)
            .ToList();
    }

    public IReadOnlyList<SkillInfo> Skills => _skills;

    public string? ToPromptSection()
    {
        if (_skills.Count == 0) return null;

        var sb = new StringBuilder();
        sb.AppendLine("## Available Skills");
        sb.AppendLine("Use the `load_skill` tool to get full instructions for a skill before applying it.");
        foreach (var skill in _skills)
            sb.AppendLine($"- **{skill.Name}**: {skill.Description}");

        return sb.ToString().TrimEnd();
    }

    public string? GetBody(string name)
    {
        var skill = _skills.FirstOrDefault(s => s.Name == name);
        return skill == null ? null : ReadBody(skill.FilePath);
    }

    private static SkillInfo? TryParseHeader(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        if (lines.Length == 0 || lines[0].Trim() != "---") return null;

        string? name = null, description = null;
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---") break;
            var line = lines[i];
            if (line.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                name = line["name:".Length..].Trim();
            else if (line.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
                description = line["description:".Length..].Trim();
        }

        if (name == null || description == null) return null;
        return new SkillInfo(name, description, filePath);
    }

    private static string ReadBody(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        bool opened = false;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() != "---") continue;
            if (!opened) { opened = true; continue; }
            // second ---: body starts on the next line
            return i + 1 < lines.Length
                ? string.Join("\n", lines[(i + 1)..]).Trim()
                : "";
        }
        return "";
    }
}
