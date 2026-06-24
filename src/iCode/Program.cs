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
var executor = new ToolExecutor(workingDir);

Console.WriteLine($"iCode agent started. Working directory: {workingDir}");
Console.WriteLine("Type '/exit' to quit.\n");

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

    // Tool-calling loop: repeat until the model sends a text reply with no tool calls
    while (true)
    {
        try
        {
            var history  = store.LoadAll().Select(ToMessage).ToList();
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

static ChatMessage ToMessage(ContextMessage m) => new(m.Role, m.Content)
{
    ToolCallsJson = m.ToolCalls,
    ToolCallId    = m.ToolCallId,
    ToolName      = m.Name
};
