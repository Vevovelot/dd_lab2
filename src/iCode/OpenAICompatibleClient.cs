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
        {
            if (IsContextLengthError(responseBody))
                throw new ContextLengthException($"Context length exceeded. Provider response: {responseBody}");
            throw new HttpRequestException($"API error {(int)response.StatusCode}: {responseBody}");
        }

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

        var (inputTokens, outputTokens) = ParseUsage(doc.RootElement);
        return new ProviderResponse(content, toolCalls, toolCallsJson, inputTokens, outputTokens);
    }

    private static (int input, int output) ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage))
            return (0, 0);

        // OpenAI: prompt_tokens/completion_tokens; Anthropic: input_tokens/output_tokens
        int input = 0, output = 0;
        if (usage.TryGetProperty("prompt_tokens", out var pt))   input  = pt.GetInt32();
        if (usage.TryGetProperty("input_tokens", out var it))    input  = it.GetInt32();
        if (usage.TryGetProperty("completion_tokens", out var ct)) output = ct.GetInt32();
        if (usage.TryGetProperty("output_tokens", out var ot))   output = ot.GetInt32();
        return (input, output);
    }

    private static bool IsContextLengthError(string responseBody)
    {
        // Covers OpenAI ("context_length_exceeded") and Anthropic ("prompt is too long")
        return responseBody.Contains("context_length_exceeded", StringComparison.OrdinalIgnoreCase)
            || responseBody.Contains("prompt is too long", StringComparison.OrdinalIgnoreCase)
            || responseBody.Contains("maximum context length", StringComparison.OrdinalIgnoreCase);
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
