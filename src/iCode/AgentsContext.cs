namespace iCode;

public class AgentsContext
{
    public string? SystemPrompt { get; }

    public AgentsContext(string workingDirectory)
    {
        var path = Path.Combine(workingDirectory, "AGENTS.md");
        if (File.Exists(path))
            SystemPrompt = File.ReadAllText(path);
    }

    public IReadOnlyList<ChatMessage> Prepend(IReadOnlyList<ChatMessage> history)
    {
        if (SystemPrompt == null)
            return history;

        var result = new List<ChatMessage>(history.Count + 1)
        {
            new("system", SystemPrompt)
        };
        result.AddRange(history);
        return result;
    }
}
