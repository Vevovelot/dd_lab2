using Microsoft.Extensions.Configuration;
using iCode;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var maxTokens = int.Parse(config["Agent:MaxTokens"] ?? "8192");
var provider  = ModelProviderFactory.Create(config, maxTokens);

var workingDir = Directory.GetCurrentDirectory();
var dbPath     = ProjectIdentity.GetContextDbPath(workingDir);

using var store   = new ContextStore(dbPath);
var agentsContext  = new AgentsContext(workingDir);
var skillsLoader   = new SkillsLoader(workingDir);

if (agentsContext.SystemPrompt != null)
    Console.WriteLine("[AGENTS.md loaded into system prompt]");
if (skillsLoader.Skills.Count > 0)
    Console.WriteLine($"[{skillsLoader.Skills.Count} skill(s) loaded from SKILLS/]");

Console.WriteLine($"iCode agent started. Working directory: {workingDir}");
Console.WriteLine("Type '/exit' to quit.\n");

// Build combined system prompt once at startup
var systemParts = new System.Collections.Generic.List<string>();
if (agentsContext.SystemPrompt != null)
    systemParts.Add(agentsContext.SystemPrompt);
var skillsSection = skillsLoader.ToPromptSection();
if (skillsSection != null)
    systemParts.Add(skillsSection);
var systemPrompt = systemParts.Count > 0 ? string.Join("\n\n", systemParts) : null;

var agentRunner = new AgentRunner(provider, workingDir, skillsLoader, systemPrompt);
var executor    = new ToolExecutor(workingDir, skillsLoader, subagentRunner: task => agentRunner.RunAsync(task));

while (true)
{
    Console.Write("You: ");
    var input = Console.ReadLine();

    if (input == null || input.Trim() == "/exit")
    {
        Console.WriteLine("Goodbye.");
        break;
    }

    if (string.IsNullOrWhiteSpace(input))
        continue;

    store.Append("user", input);

    while (true)
    {
        try
        {
            var history = BuildHistory(systemPrompt, store.LoadAll().Select(ToMessage).ToList());
            var response = await provider.SendAsync(history, ToolDefinitions.All);

            if (response.ToolCalls == null || response.ToolCalls.Count == 0)
            {
                store.Append("assistant", response.Content);
                Console.WriteLine($"\nAssistant: {response.Content}\n");
                break;
            }

            store.AppendAssistantToolCalls(response.ToolCallsJson!);

            foreach (var toolCall in response.ToolCalls)
            {
                Console.WriteLine($"[Tool: {toolCall.Name}]");
                var result  = await executor.ExecuteAsync(toolCall.Name, toolCall.Arguments);
                var preview = result.Length > 300 ? result[..300] + "…" : result;
                Console.WriteLine($"[Result: {preview}]");
                store.AppendToolResult(toolCall.Id, toolCall.Name, result);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            break;
        }
    }
}

static IReadOnlyList<ChatMessage> BuildHistory(string? systemPrompt, List<ChatMessage> history)
{
    if (systemPrompt == null) return history;
    var result = new List<ChatMessage>(history.Count + 1) { new("system", systemPrompt) };
    result.AddRange(history);
    return result;
}

static ChatMessage ToMessage(ContextMessage m) => new(m.Role, m.Content)
{
    ToolCallsJson = m.ToolCalls,
    ToolCallId    = m.ToolCallId,
    ToolName      = m.Name
};
