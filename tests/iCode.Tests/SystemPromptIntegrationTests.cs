using System.Net;
using System.Text;
using System.Text.Json;
using iCode;

namespace iCode.Tests;

/// <summary>
/// Intercepts outgoing HTTP requests and captures the request body.
/// </summary>
public class CapturingHandler : HttpMessageHandler
{
    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        LastRequestBody = await request.Content!.ReadAsStringAsync(ct);

        var fakeBody = """
            {
              "choices": [{
                "message": { "role": "assistant", "content": "ok" }
              }]
            }
            """;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(fakeBody, Encoding.UTF8, "application/json")
        };
    }
}

public class SystemPromptIntegrationTests : IDisposable
{
    private readonly string _workDir;

    public SystemPromptIntegrationTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "icode_sysprompt_test_" + Guid.NewGuid());
        Directory.CreateDirectory(_workDir);
    }

    [Fact]
    public async Task AgentsMd_AppearsAsFirstSystemMessage_InHttpRequest()
    {
        // Arrange
        const string agentsContent = "You are an expert coding agent.";
        File.WriteAllText(Path.Combine(_workDir, "AGENTS.md"), agentsContent);

        var handler = new CapturingHandler();
        var client  = new OpenAICompatibleClient(
            new HttpClient(handler), "http://test/v1", "test-model", 128);

        var agentsCtx = new AgentsContext(_workDir);
        var userHistory = new List<ChatMessage> { new("user", "hello") };
        var history = agentsCtx.Prepend(userHistory);

        // Act
        await client.SendAsync(history);

        // Assert — parse the JSON that actually left the process
        Assert.NotNull(handler.LastRequestBody);
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var messages = doc.RootElement.GetProperty("messages");

        Assert.True(messages.GetArrayLength() >= 2, "Expected at least system + user messages");

        var first = messages[0];
        Assert.Equal("system", first.GetProperty("role").GetString());
        Assert.Equal(agentsContent, first.GetProperty("content").GetString());

        var second = messages[1];
        Assert.Equal("user", second.GetProperty("role").GetString());
        Assert.Equal("hello", second.GetProperty("content").GetString());
    }

    [Fact]
    public async Task NoAgentsMd_NoSystemMessage_InHttpRequest()
    {
        // Arrange — no AGENTS.md in workDir
        var handler = new CapturingHandler();
        var client  = new OpenAICompatibleClient(
            new HttpClient(handler), "http://test/v1", "test-model", 128);

        var agentsCtx = new AgentsContext(_workDir);
        var history = agentsCtx.Prepend(new List<ChatMessage> { new("user", "hello") });

        // Act
        await client.SendAsync(history);

        // Assert
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var messages = doc.RootElement.GetProperty("messages");

        Assert.Equal(1, messages.GetArrayLength());
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_workDir))
            Directory.Delete(_workDir, recursive: true);
    }
}
