using iCode;

namespace iCode.Tests;

public class AgentsContextTests : IDisposable
{
    private readonly string _workDir;

    public AgentsContextTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "icode_agents_test_" + Guid.NewGuid());
        Directory.CreateDirectory(_workDir);
    }

    [Fact]
    public void SystemPrompt_IsNull_WhenNoAgentsMd()
    {
        var ctx = new AgentsContext(_workDir);
        Assert.Null(ctx.SystemPrompt);
    }

    [Fact]
    public void SystemPrompt_ContentsOfAgentsMd_WhenFileExists()
    {
        File.WriteAllText(Path.Combine(_workDir, "AGENTS.md"), "You are a helpful agent.");
        var ctx = new AgentsContext(_workDir);
        Assert.Equal("You are a helpful agent.", ctx.SystemPrompt);
    }

    [Fact]
    public void Prepend_NoSystemPrompt_ReturnsSameHistory()
    {
        var ctx = new AgentsContext(_workDir);
        var history = new List<ChatMessage> { new("user", "hi") };
        var result = ctx.Prepend(history);
        Assert.Single(result);
        Assert.Equal("user", result[0].Role);
    }

    [Fact]
    public void Prepend_WithSystemPrompt_InsertsFirstMessage()
    {
        File.WriteAllText(Path.Combine(_workDir, "AGENTS.md"), "Be concise.");
        var ctx = new AgentsContext(_workDir);
        var history = new List<ChatMessage>
        {
            new("user", "hello"),
            new("assistant", "hi")
        };

        var result = ctx.Prepend(history);

        Assert.Equal(3, result.Count);
        Assert.Equal("system", result[0].Role);
        Assert.Equal("Be concise.", result[0].Content);
        Assert.Equal("user", result[1].Role);
        Assert.Equal("assistant", result[2].Role);
    }

    [Fact]
    public void Prepend_DoesNotModifyOriginalHistory()
    {
        File.WriteAllText(Path.Combine(_workDir, "AGENTS.md"), "system text");
        var ctx = new AgentsContext(_workDir);
        var history = new List<ChatMessage> { new("user", "hi") };
        ctx.Prepend(history);
        Assert.Single(history);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workDir))
            Directory.Delete(_workDir, recursive: true);
    }
}
