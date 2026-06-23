using Microsoft.Extensions.Configuration;

namespace iCode;

public static class ModelProviderFactory
{
    public static IModelProvider Create(IConfiguration config, int maxTokens)
    {
        var activeProvider = config["Agent:ActiveProvider"]
            ?? throw new InvalidOperationException("Agent:ActiveProvider not configured");

        var section = $"Agent:Providers:{activeProvider}";
        var type = config[$"{section}:Type"]
            ?? throw new InvalidOperationException($"Provider '{activeProvider}': Type not configured");
        var baseUrl = config[$"{section}:BaseUrl"]
            ?? throw new InvalidOperationException($"Provider '{activeProvider}': BaseUrl not configured");
        var model = config[$"{section}:Model"]
            ?? throw new InvalidOperationException($"Provider '{activeProvider}': Model not configured");
        var apiKeyEnv = config[$"{section}:ApiKeyEnv"];

        string? apiKey = null;
        if (!string.IsNullOrEmpty(apiKeyEnv))
        {
            apiKey = Environment.GetEnvironmentVariable(apiKeyEnv)
                ?? throw new InvalidOperationException($"Environment variable '{apiKeyEnv}' is not set");
        }

        return type switch
        {
            "openai-compatible" => new OpenAICompatibleClient(baseUrl, model, maxTokens, apiKey),
            _ => throw new NotSupportedException($"Provider type '{type}' is not supported")
        };
    }
}
