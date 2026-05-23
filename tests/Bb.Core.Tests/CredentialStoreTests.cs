using System;
using System.IO;
using System.Runtime.InteropServices;
using Bb.Core.Services;
using Xunit;

namespace Bb.Core.Tests;

/// <summary>
/// CredentialStore uses DPAPI, which is Windows-only. We gate the tests at
/// runtime so they only execute on Windows (Linux CI would crash on the
/// ProtectedData.Protect call).
/// </summary>
[Collection("Bb home env")]
public sealed class CredentialStoreTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string? _originalBbHome;

    public CredentialStoreTests()
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

    private static bool OnWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    [Fact]
    public void RoundTripsSecret()
    {
        if (!OnWindows) return;

        CredentialStore.SetSecret("openai", "sk-test-1234567890");
        var got = CredentialStore.GetSecret("openai");
        Assert.Equal("sk-test-1234567890", got);
    }

    [Fact]
    public void ReturnsNullForUnknownProvider()
    {
        if (!OnWindows) return;
        Assert.Null(CredentialStore.GetSecret("nonexistent"));
    }

    [Fact]
    public void OverwritesExistingSecret()
    {
        if (!OnWindows) return;

        CredentialStore.SetSecret("openai", "first-key");
        CredentialStore.SetSecret("openai", "second-key");
        Assert.Equal("second-key", CredentialStore.GetSecret("openai"));
    }

    [Fact]
    public void StoresMultipleProvidersIndependently()
    {
        if (!OnWindows) return;

        CredentialStore.SetSecret("openai", "k1");
        CredentialStore.SetSecret("ollama", "k2");
        Assert.Equal("k1", CredentialStore.GetSecret("openai"));
        Assert.Equal("k2", CredentialStore.GetSecret("ollama"));
    }

    [Fact]
    public void IsCaseInsensitiveByProviderName()
    {
        if (!OnWindows) return;

        CredentialStore.SetSecret("OpenAI", "k");
        Assert.Equal("k", CredentialStore.GetSecret("openai"));
        Assert.Equal("k", CredentialStore.GetSecret("OPENAI"));
    }

    [Fact]
    public void RemoveSecretIsIdempotent()
    {
        if (!OnWindows) return;

        CredentialStore.RemoveSecret("does-not-exist");          // no-throw
        CredentialStore.SetSecret("temp", "k");
        CredentialStore.RemoveSecret("temp");
        Assert.Null(CredentialStore.GetSecret("temp"));
        CredentialStore.RemoveSecret("temp");                    // double-remove no-throw
    }

    [Fact]
    public void StoredFileDoesNotContainPlaintext()
    {
        if (!OnWindows) return;

        var marker = "this-must-not-appear-on-disk-" + Guid.NewGuid().ToString("N");
        CredentialStore.SetSecret("paranoid", marker);

        var path = BbPaths.GetCredentialsPath();
        var contents = File.ReadAllText(path);
        Assert.DoesNotContain(marker, contents);
    }

    [Fact]
    public void EmptyProviderNameThrows()
    {
        if (!OnWindows) return;

        Assert.Throws<ArgumentException>(() => CredentialStore.SetSecret(" ", "k"));
        Assert.Throws<ArgumentException>(() => CredentialStore.GetSecret(""));
        Assert.Throws<ArgumentException>(() => CredentialStore.RemoveSecret(""));
    }
}
