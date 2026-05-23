using System;
using System.IO;
using Bb.Core.Models;
using Bb.Core.Services;
using Xunit;

namespace Bb.Core.Tests;

[Collection("Bb home env")]
public sealed class BbConfigStoreTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string? _originalBbHome;

    public BbConfigStoreTests()
    {
        _originalBbHome = Environment.GetEnvironmentVariable("BB_HOME");
        _tempRoot = Path.Combine(Path.GetTempPath(), "bb-test-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("BB_HOME", _tempRoot);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BB_HOME", _originalBbHome);
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public void LoadReturnsEmptyConfigWhenFileMissing()
    {
        var cfg = BbConfigStore.Load();
        Assert.NotNull(cfg);
        Assert.Null(cfg.ActiveProvider);
        Assert.Empty(cfg.Providers);
    }

    [Fact]
    public void SaveAndLoadRoundTripsProviders()
    {
        var written = new BbConfig
        {
            ActiveProvider = "openai",
            Providers =
            {
                ["openai"] = new ProviderConfig
                {
                    Name = "openai",
                    Endpoint = "https://api.openai.com/v1/chat/completions",
                    Model = "gpt-4o-mini",
                    Stream = true,
                    TimeoutSeconds = 8,
                },
                ["ollama"] = new ProviderConfig
                {
                    Name = "ollama",
                    Endpoint = "http://localhost:11434/v1/chat/completions",
                    Model = "llama3.2",
                    Stream = false,
                    TimeoutSeconds = 30,
                },
            },
        };

        BbConfigStore.Save(written);
        var read = BbConfigStore.Load();

        Assert.Equal("openai", read.ActiveProvider);
        Assert.Equal(2, read.Providers.Count);
        Assert.Equal("gpt-4o-mini", read.Providers["openai"].Model);
        Assert.False(read.Providers["ollama"].Stream);
        Assert.Equal(30, read.Providers["ollama"].TimeoutSeconds);
    }

    [Fact]
    public void ProviderNamesAreCaseInsensitive()
    {
        var written = new BbConfig
        {
            ActiveProvider = "openai",
            Providers = { ["OpenAI"] = new ProviderConfig { Name = "OpenAI" } },
        };
        BbConfigStore.Save(written);

        var read = BbConfigStore.Load();
        Assert.True(read.Providers.ContainsKey("openai"));
        Assert.True(read.Providers.ContainsKey("OPENAI"));
    }

    [Fact]
    public void CorruptConfigThrowsHelpfulError()
    {
        var path = BbPaths.GetConfigPath();
        File.WriteAllText(path, "{ not valid json");
        var ex = Assert.Throws<InvalidDataException>(() => BbConfigStore.Load());
        Assert.Contains("config.json", ex.Message);
    }

    [Fact]
    public void SaveIsAtomicWithNoTempLeftBehind()
    {
        var cfg = new BbConfig { ActiveProvider = "x", Providers = { ["x"] = new ProviderConfig { Name = "x" } } };
        BbConfigStore.Save(cfg);

        var dir = Path.GetDirectoryName(BbPaths.GetConfigPath())!;
        var lingering = Directory.GetFiles(dir, "config.json.tmp");
        Assert.Empty(lingering);
    }
}
