using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace iCode;

public class OpenAICompatibleClient : IModelProvider
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly int _maxTokens;
    private readonly string _baseUrl;

    public OpenAICompatibleClient(string baseUrl, string model, int maxTokens, string? apiKey)
        : this(CreateHttpClient(apiKey), baseUrl, model, maxTokens) { }

    public OpenAICompatibleClient(HttpClient httpClient, string baseUrl, string model, int maxTokens)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
        _maxTokens = maxTokens;
        _http = httpClient;
    }

    private static HttpClient CreateHttpClient(string? apiKey)
    {
        var client = new HttpClient();
        if (!string.IsNullOrEmpty(apiKey))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public async Task<ProviderResponse> SendAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<JsonObject>? tools = null,
        CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["model"] = _model,
            ["max_tokens"] = _maxTokens,
            ["messages"] = new JsonArray(messages.Select(SerializeMessage).ToArray())
        };

        if (tools != null && tools.Count > 0)
            body["tools"] = new JsonArray(tools.Select(t => (JsonNode)t.DeepClone()).ToArray());

        var json = body.ToJsonString();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"API error {(int)response.StatusCode}: {responseBody}");

        using var doc = JsonDocument.Parse(responseBody);
        var message = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message");

        var content = message.TryGetProperty("content", out var contentEl) && contentEl.ValueKind != JsonValueKind.Null
            ? contentEl.GetString() ?? string.Empty
            : string.Empty;

        string? toolCallsJson = null;
        List<ToolCall>? toolCalls = null;
        if (message.TryGetProperty("tool_calls", out var toolCallsEl) && toolCallsEl.ValueKind == JsonValueKind.Array)
        {
            toolCallsJson = toolCallsEl.GetRawText();
            toolCalls = toolCallsEl.EnumerateArray().Select(tc => new ToolCall(
                tc.GetProperty("id").GetString() ?? string.Empty,
                tc.GetProperty("function").GetProperty("name").GetString() ?? string.Empty,
                tc.GetProperty("function").GetProperty("arguments").GetString() ?? string.Empty
            )).ToList();
        }

        return new ProviderResponse(content, toolCalls, toolCallsJson);
    }

    private static JsonNode SerializeMessage(ChatMessage m)
    {
        var obj = new JsonObject { ["role"] = m.Role };

        if (m.ToolCallsJson != null)
        {
            obj["content"] = (JsonNode?)null;
            obj["tool_calls"] = JsonNode.Parse(m.ToolCallsJson);
        }
        else if (m.ToolCallId != null)
        {
            obj["content"] = m.Content ?? string.Empty;
            obj["tool_call_id"] = m.ToolCallId;
        }
        else
        {
            obj["content"] = m.Content ?? string.Empty;
        }

        return obj;
    }
}
