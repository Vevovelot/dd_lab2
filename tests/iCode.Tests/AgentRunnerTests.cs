using NSubstitute;
using System.Text.Json.Nodes;

namespace iCode.Tests;

public class AgentRunnerTests : IDisposable
{
    private readonly string _workDir;

    public AgentRunnerTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "agentrunner_test_" + Guid.NewGuid());
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose() => Directory.Delete(_workDir, recursive: true);

    [Fact]
    public async Task RunAsync_ReturnsDirectResponse_WhenNoToolCalls()
    {
        var provider = Substitute.For<IModelProvider>();
        provider.SendAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<JsonObject>>(), Arg.Any<CancellationToken>())
            .Returns(new ProviderResponse("Task complete"));

        var runner = new AgentRunner(provider, _workDir, null, null);
        var result = await runner.RunAsync("Do something");

        Assert.Equal("Task complete", result);
    }

    [Fact]
    public async Task RunAsync_SystemPromptIncludedAsFirstMessage()
    {
        IReadOnlyList<ChatMessage>? capturedMessages = null;
        var provider = Substitute.For<IModelProvider>();
        provider.SendAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<JsonObject>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                capturedMessages = ci.Arg<IReadOnlyList<ChatMessage>>();
                return new ProviderResponse("done");
            });

        var runner = new AgentRunner(provider, _workDir, null, "You are a helpful assistant.");
        await runner.RunAsync("Hello");

        Assert.NotNull(capturedMessages);
        Assert.Equal("system", capturedMessages![0].Role);
        Assert.Equal("You are a helpful assistant.", capturedMessages[0].Content);
        Assert.Equal("user", capturedMessages[1].Role);
        Assert.Equal("Hello", capturedMessages[1].Content);
    }

    [Fact]
    public async Task RunAsync_ExecutesToolAndReturnsAfterFinalResponse()
    {
        File.WriteAllText(Path.Combine(_workDir, "notes.txt"), "hello");

        var callCount = 0;
        var provider = Substitute.For<IModelProvider>();
        provider.SendAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<JsonObject>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                callCount++;
                if (callCount == 1)
                {
                    var toolCall = new ToolCall("id1", "list_files", "{}");
                    var toolCallsJson = """[{"id":"id1","type":"function","function":{"name":"list_files","arguments":"{}"}}]""";
                    return new ProviderResponse(string.Empty, new[] { toolCall }, toolCallsJson);
                }
                return new ProviderResponse("Files listed successfully");
            });

        var runner = new AgentRunner(provider, _workDir, null, null);
        var result = await runner.RunAsync("List files");

        Assert.Equal("Files listed successfully", result);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task RunAsync_UsesWithoutSubagentTools_PreventingNesting()
    {
        IReadOnlyList<JsonObject>? capturedTools = null;
        var provider = Substitute.For<IModelProvider>();
        provider.SendAsync(Arg.Any<IReadOnlyList<ChatMessage>>(), Arg.Any<IReadOnlyList<JsonObject>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                capturedTools = ci.Arg<IReadOnlyList<JsonObject>>();
                return new ProviderResponse("done");
            });

        var runner = new AgentRunner(provider, _workDir, null, null);
        await runner.RunAsync("task");

        Assert.NotNull(capturedTools);
        var toolNames = capturedTools!
            .Select(t => t["function"]?["name"]?.GetValue<string>())
            .ToList();
        Assert.DoesNotContain("run_subagent", toolNames);
    }

    [Fact]
    public async Task ToolExecutor_RunSubagent_ReturnsError_WhenNotConfigured()
    {
        var executor = new ToolExecutor(_workDir);
        var result = await executor.ExecuteAsync("run_subagent", """{"task":"nested task"}""");
        Assert.Contains("Error", result);
        Assert.Contains("not available", result);
    }

    [Fact]
    public async Task ToolExecutor_RunSubagent_CallsRunner_AndReturnsResult()
    {
        var runner = (string task) => Task.FromResult($"subagent result for: {task}");
        var executor = new ToolExecutor(_workDir, null, subagentRunner: runner);

        var result = await executor.ExecuteAsync("run_subagent", """{"task":"analyze code"}""");

        Assert.Equal("subagent result for: analyze code", result);
    }
}
