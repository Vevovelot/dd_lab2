using System.Text.Json.Nodes;

namespace iCode;

public class AgentRunner
{
    private readonly IModelProvider _provider;
    private readonly string _workingDirectory;
    private readonly SkillsLoader? _skillsLoader;
    private readonly string? _systemPrompt;
    private readonly PermissionManager? _permissions;

    public AgentRunner(
        IModelProvider provider,
        string workingDirectory,
        SkillsLoader? skillsLoader,
        string? systemPrompt,
        PermissionManager? permissions = null)
    {
        _provider = provider;
        _workingDirectory = workingDirectory;
        _skillsLoader = skillsLoader;
        _systemPrompt = systemPrompt;
        _permissions = permissions;
    }

    // Runs a task with clean in-memory context; returns the final assistant response.
    public async Task<string> RunAsync(string task, CancellationToken ct = default)
    {
        // Subagent: no subagent runner (prevents nesting); shares PermissionManager (non-interactive).
        var executor = new ToolExecutor(_workingDirectory, _skillsLoader, permissions: _permissions);

        var messages = new List<ChatMessage>();
        if (_systemPrompt != null)
            messages.Add(new ChatMessage("system", _systemPrompt));
        messages.Add(new ChatMessage("user", task));

        while (true)
        {
            var response = await _provider.SendAsync(messages, ToolDefinitions.WithoutSubagent, ct);

            if (response.ToolCalls == null || response.ToolCalls.Count == 0)
            {
                messages.Add(new ChatMessage("assistant", response.Content));
                return response.Content ?? string.Empty;
            }

            messages.Add(new ChatMessage("assistant", null) { ToolCallsJson = response.ToolCallsJson });

            foreach (var toolCall in response.ToolCalls)
            {
                var result = await executor.ExecuteAsync(toolCall.Name, toolCall.Arguments);
                messages.Add(new ChatMessage("tool", result)
                {
                    ToolCallId = toolCall.Id,
                    ToolName   = toolCall.Name
                });
            }
        }
    }
}
