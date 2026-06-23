using Microsoft.Extensions.Configuration;
using iCode;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var maxTokens = int.Parse(config["Agent:MaxTokens"] ?? "8192");
var provider = ModelProviderFactory.Create(config, maxTokens);

var workingDir = Directory.GetCurrentDirectory();
var dbPath = ProjectIdentity.GetContextDbPath(workingDir);

using var store = new ContextStore(dbPath);

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

    try
    {
        var history = store.LoadAll()
            .Select(m => new ChatMessage(m.Role, m.Content))
            .ToList();
        var response = await provider.SendAsync(history);
        store.Append("assistant", response.Content);
        Console.WriteLine($"\nAssistant: {response.Content}\n");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
    }
}
