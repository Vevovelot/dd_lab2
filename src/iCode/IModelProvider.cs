namespace iCode;

public record ChatMessage(string Role, string Content);
public record ToolCall(string Id, string Name, string Arguments);
public record ProviderResponse(string Content, IReadOnlyList<ToolCall>? ToolCalls = null);

public interface IModelProvider
{
    Task<ProviderResponse> SendAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct = default);
}
