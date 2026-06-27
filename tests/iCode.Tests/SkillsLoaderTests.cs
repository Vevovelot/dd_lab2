using iCode;

namespace iCode.Tests;

public class SkillsLoaderTests : IDisposable
{
    private readonly string _workDir;
    private readonly string _skillsDir;

    public SkillsLoaderTests()
    {
        _workDir   = Path.Combine(Path.GetTempPath(), "icode_skills_test_" + Guid.NewGuid());
        _skillsDir = Path.Combine(_workDir, "SKILLS");
        Directory.CreateDirectory(_skillsDir);
    }

    private void WriteSkill(string filename, string content) =>
        File.WriteAllText(Path.Combine(_skillsDir, filename), content);

    // --- Parsing ---

    [Fact]
    public void ParsesNameDescriptionAndBody()
    {
        WriteSkill("test.md", """
            ---
            name: my-skill
            description: Use when you need X
            ---

            Step 1: do this.
            Step 2: do that.
            """);

        var loader = new SkillsLoader(_workDir);

        Assert.Single(loader.Skills);
        Assert.Equal("my-skill",          loader.Skills[0].Name);
        Assert.Equal("Use when you need X", loader.Skills[0].Description);
    }

    [Fact]
    public void GetBody_ReturnsBodyContent()
    {
        WriteSkill("s.md", """
            ---
            name: s
            description: desc
            ---
            Do the thing.
            """);

        var loader = new SkillsLoader(_workDir);
        var body = loader.GetBody("s");

        Assert.NotNull(body);
        Assert.Contains("Do the thing.", body);
    }

    [Fact]
    public void GetBody_DoesNotIncludeFrontmatter()
    {
        WriteSkill("s.md", """
            ---
            name: s
            description: desc
            ---
            Body only.
            """);

        var body = new SkillsLoader(_workDir).GetBody("s");
        Assert.DoesNotContain("description:", body);
        Assert.DoesNotContain("---", body);
    }

    [Fact]
    public void SkipFile_WhenNoName()
    {
        WriteSkill("bad.md", """
            ---
            description: no name here
            ---
            Body.
            """);

        var loader = new SkillsLoader(_workDir);
        Assert.Empty(loader.Skills);
    }

    [Fact]
    public void SkipFile_WhenNoFrontmatter()
    {
        WriteSkill("plain.md", "Just plain markdown, no frontmatter.");
        Assert.Empty(new SkillsLoader(_workDir).Skills);
    }

    [Fact]
    public void LoadsMultipleSkills()
    {
        WriteSkill("a.md", "---\nname: alpha\ndescription: A\n---\nBody A.");
        WriteSkill("b.md", "---\nname: beta\ndescription: B\n---\nBody B.");

        var loader = new SkillsLoader(_workDir);
        Assert.Equal(2, loader.Skills.Count);
    }

    // --- No SKILLS dir ---

    [Fact]
    public void NoSkillsDir_ReturnsEmpty()
    {
        var emptyWork = Path.Combine(Path.GetTempPath(), "icode_noskills_" + Guid.NewGuid());
        Directory.CreateDirectory(emptyWork);
        try
        {
            Assert.Empty(new SkillsLoader(emptyWork).Skills);
        }
        finally { Directory.Delete(emptyWork); }
    }

    // --- ToPromptSection ---

    [Fact]
    public void ToPromptSection_ContainsNamesAndDescriptions()
    {
        WriteSkill("s.md", "---\nname: do-thing\ndescription: How to do a thing\n---\nBody.");

        var section = new SkillsLoader(_workDir).ToPromptSection();

        Assert.NotNull(section);
        Assert.Contains("do-thing",          section);
        Assert.Contains("How to do a thing", section);
        Assert.DoesNotContain("Body.",        section);
    }

    [Fact]
    public void ToPromptSection_ReturnsNull_WhenNoSkills()
    {
        Assert.Null(new SkillsLoader(_workDir).ToPromptSection());
    }

    // --- load_skill tool ---

    [Fact]
    public async Task LoadSkillTool_ReturnsBody()
    {
        WriteSkill("s.md", "---\nname: my-skill\ndescription: desc\n---\nFull instructions here.");
        var loader   = new SkillsLoader(_workDir);
        var executor = new ToolExecutor(_workDir, loader);

        var result = await executor.ExecuteAsync("load_skill", """{"name":"my-skill"}""");
        Assert.Contains("Full instructions here.", result);
    }

    [Fact]
    public async Task LoadSkillTool_UnknownSkill_ReturnsError()
    {
        var loader   = new SkillsLoader(_workDir);
        var executor = new ToolExecutor(_workDir, loader);

        var result = await executor.ExecuteAsync("load_skill", """{"name":"ghost"}""");
        Assert.StartsWith("Error:", result);
    }

    [Fact]
    public async Task LoadSkillTool_NoSkillsLoader_ReturnsError()
    {
        var executor = new ToolExecutor(_workDir); // no skillsLoader
        var result = await executor.ExecuteAsync("load_skill", """{"name":"anything"}""");
        Assert.StartsWith("Error:", result);
    }

    // --- GetBody unknown ---

    [Fact]
    public void GetBody_UnknownName_ReturnsNull()
    {
        Assert.Null(new SkillsLoader(_workDir).GetBody("nonexistent"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_workDir))
            Directory.Delete(_workDir, recursive: true);
    }
}
