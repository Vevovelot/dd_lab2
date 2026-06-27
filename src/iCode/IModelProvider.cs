using System.Text.Json.Nodes;

namespace iCode;

public record ChatMessage(string Role, string? Content)
{
    public string? ToolCallsJson { get; init; }
    public string? ToolCallId   { get; init; }
    public string? ToolName     { get; init; }
}

public record ToolCall(string Id, string Name, string Arguments);

public record ProviderResponse(
    string Content,
    IReadOnlyList<ToolCall>? ToolCalls = null,
    string? ToolCallsJson = null,
    int InputTokens = 0,
    int OutputTokens = 0
);

public class ContextLengthException : Exception
{
    public ContextLengthException(string message) : base(message) { }
}

public interface IModelProvider
{
    Task<ProviderResponse> SendAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<JsonObject>? tools = null,
        CancellationToken ct = default);
}
