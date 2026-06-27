using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace iCode.Tests;

public class OpenAICompatibleClientTests
{
    private static iCode.OpenAICompatibleClient MakeClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler), "http://fake/v1", "test-model", 1024);

    private static HttpMessageHandler FakeResponse(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new FakeHttpHandler(status, json);

    // --- usage parsing ---

    [Fact]
    public async Task ParsesOpenAIUsage_PromptAndCompletionTokens()
    {
        var json = """
            {
              "choices": [{"message": {"role":"assistant","content":"hi"}}],
              "usage": {"prompt_tokens": 100, "completion_tokens": 50}
            }
            """;
        var client = MakeClient(FakeResponse(json));
        var response = await client.SendAsync(new[] { new ChatMessage("user", "hello") });

        Assert.Equal(100, response.InputTokens);
        Assert.Equal(50, response.OutputTokens);
    }

    [Fact]
    public async Task ParsesAnthropicUsage_InputAndOutputTokens()
    {
        var json = """
            {
              "choices": [{"message": {"role":"assistant","content":"hi"}}],
              "usage": {"input_tokens": 200, "output_tokens": 75}
            }
            """;
        var client = MakeClient(FakeResponse(json));
        var response = await client.SendAsync(new[] { new ChatMessage("user", "hello") });

        Assert.Equal(200, response.InputTokens);
        Assert.Equal(75, response.OutputTokens);
    }

    [Fact]
    public async Task ReturnsZeroTokens_WhenUsageMissing()
    {
        var json = """{"choices": [{"message": {"role":"assistant","content":"hi"}}]}""";
        var client = MakeClient(FakeResponse(json));
        var response = await client.SendAsync(new[] { new ChatMessage("user", "hello") });

        Assert.Equal(0, response.InputTokens);
        Assert.Equal(0, response.OutputTokens);
    }

    // --- context length error detection ---

    [Fact]
    public async Task ThrowsContextLengthException_WhenOpenAIContextLengthExceeded()
    {
        var errorJson = """{"error":{"code":"context_length_exceeded","message":"This model maximum context length is 8192 tokens."}}""";
        var client = MakeClient(FakeResponse(errorJson, HttpStatusCode.BadRequest));

        await Assert.ThrowsAsync<ContextLengthException>(
            () => client.SendAsync(new[] { new ChatMessage("user", "hello") }));
    }

    [Fact]
    public async Task ThrowsContextLengthException_WhenAnthropicPromptTooLong()
    {
        var errorJson = """{"error":{"type":"invalid_request_error","message":"prompt is too long: 9000 tokens > 8000 maximum"}}""";
        var client = MakeClient(FakeResponse(errorJson, HttpStatusCode.BadRequest));

        await Assert.ThrowsAsync<ContextLengthException>(
            () => client.SendAsync(new[] { new ChatMessage("user", "hello") }));
    }

    [Fact]
    public async Task ThrowsHttpRequestException_ForOtherApiErrors()
    {
        var errorJson = """{"error":{"message":"Invalid API key"}}""";
        var client = MakeClient(FakeResponse(errorJson, HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.SendAsync(new[] { new ChatMessage("user", "hello") }));
    }
}

file sealed class FakeHttpHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}
