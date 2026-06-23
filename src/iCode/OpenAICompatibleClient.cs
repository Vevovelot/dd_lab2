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
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
        _maxTokens = maxTokens;
        _http = new HttpClient();
        if (!string.IsNullOrEmpty(apiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<ProviderResponse> SendAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["model"] = _model,
            ["max_tokens"] = _maxTokens,
            ["messages"] = new JsonArray(messages.Select(m => (JsonNode)new JsonObject
            {
                ["role"] = m.Role,
                ["content"] = m.Content
            }).ToArray())
        };

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

        var content = message.GetProperty("content").GetString() ?? string.Empty;

        List<ToolCall>? toolCalls = null;
        if (message.TryGetProperty("tool_calls", out var toolCallsEl) && toolCallsEl.ValueKind == JsonValueKind.Array)
        {
            toolCalls = toolCallsEl.EnumerateArray().Select(tc => new ToolCall(
                tc.GetProperty("id").GetString() ?? string.Empty,
                tc.GetProperty("function").GetProperty("name").GetString() ?? string.Empty,
                tc.GetProperty("function").GetProperty("arguments").GetString() ?? string.Empty
            )).ToList();
        }

        return new ProviderResponse(content, toolCalls);
    }
}
