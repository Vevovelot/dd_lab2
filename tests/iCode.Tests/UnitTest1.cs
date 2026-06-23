using iCode;
using Microsoft.Extensions.Configuration;

namespace iCode.Tests;

public class ContextStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ContextStore _store;

    public ContextStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "icode_test_" + Guid.NewGuid() + ".db");
        _store = new ContextStore(_dbPath);
    }

    [Fact]
    public void Append_StoresMessages_InOrder()
    {
        _store.Append("user", "Hello");
        _store.Append("assistant", "Hi there");
        _store.Append("user", "How are you?");

        var messages = _store.LoadAll();

        Assert.Equal(3, messages.Count);
        Assert.Equal("user", messages[0].Role);
        Assert.Equal("Hello", messages[0].Content);
        Assert.Equal("assistant", messages[1].Role);
        Assert.Equal("Hi there", messages[1].Content);
        Assert.Equal("user", messages[2].Role);
    }

    [Fact]
    public void LoadAll_ReturnsEmpty_WhenNoMessages()
    {
        var messages = _store.LoadAll();
        Assert.Empty(messages);
    }

    [Fact]
    public void Append_PersistsAcrossInstances()
    {
        _store.Append("user", "persistent message");
        _store.Dispose();

        using var store2 = new ContextStore(_dbPath);
        var messages = store2.LoadAll();

        Assert.Single(messages);
        Assert.Equal("persistent message", messages[0].Content);
    }

    public void Dispose()
    {
        _store.Dispose();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}

public class ProjectIdentityTests
{
    [Fact]
    public void GetProjectName_SamePath_ReturnsSameHash()
    {
        var name1 = ProjectIdentity.GetProjectName("/home/user/myproject");
        var name2 = ProjectIdentity.GetProjectName("/home/user/myproject");
        Assert.Equal(name1, name2);
    }

    [Fact]
    public void GetProjectName_DifferentPaths_ReturnDifferentHashes()
    {
        var name1 = ProjectIdentity.GetProjectName("/home/user/project1");
        var name2 = ProjectIdentity.GetProjectName("/home/user/project2");
        Assert.NotEqual(name1, name2);
    }

    [Fact]
    public void GetProjectName_ReturnsHexString()
    {
        var name = ProjectIdentity.GetProjectName("/some/path");
        Assert.Matches("^[0-9a-f]+$", name);
    }

    [Fact]
    public void GetContextDbPath_ContainsProjectName()
    {
        var path = "/home/user/project";
        var projectName = ProjectIdentity.GetProjectName(path);
        var dbPath = ProjectIdentity.GetContextDbPath(path);
        Assert.Contains(projectName, dbPath);
        Assert.Contains(".iCode", dbPath);
        Assert.Contains("context", dbPath);
    }
}

public class ModelProviderFactoryTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Create_OpenAICompatible_WithoutKey_ReturnsProvider()
    {
        var config = BuildConfig(new()
        {
            ["Agent:ActiveProvider"] = "local",
            ["Agent:Providers:local:Type"] = "openai-compatible",
            ["Agent:Providers:local:BaseUrl"] = "http://localhost:11434/v1/",
            ["Agent:Providers:local:Model"] = "llama3",
            ["Agent:Providers:local:ApiKeyEnv"] = null
        });

        var provider = ModelProviderFactory.Create(config, 1024);
        Assert.IsType<OpenAICompatibleClient>(provider);
    }

    [Fact]
    public void Create_MissingActiveProvider_Throws()
    {
        var config = BuildConfig(new());
        Assert.Throws<InvalidOperationException>(() => ModelProviderFactory.Create(config, 1024));
    }

    [Fact]
    public void Create_UnsupportedType_Throws()
    {
        var config = BuildConfig(new()
        {
            ["Agent:ActiveProvider"] = "x",
            ["Agent:Providers:x:Type"] = "anthropic-native",
            ["Agent:Providers:x:BaseUrl"] = "https://api.anthropic.com/",
            ["Agent:Providers:x:Model"] = "claude-3"
        });

        Assert.Throws<NotSupportedException>(() => ModelProviderFactory.Create(config, 1024));
    }

    [Fact]
    public void Create_MissingApiKeyEnvVar_Throws()
    {
        var config = BuildConfig(new()
        {
            ["Agent:ActiveProvider"] = "p",
            ["Agent:Providers:p:Type"] = "openai-compatible",
            ["Agent:Providers:p:BaseUrl"] = "https://api.openai.com/v1/",
            ["Agent:Providers:p:Model"] = "gpt-4o",
            ["Agent:Providers:p:ApiKeyEnv"] = "NONEXISTENT_VAR_XYZ_12345"
        });

        Assert.Throws<InvalidOperationException>(() => ModelProviderFactory.Create(config, 1024));
    }
}
