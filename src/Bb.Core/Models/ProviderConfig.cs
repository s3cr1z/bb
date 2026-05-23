namespace Bb.Core.Models;

/// <summary>
/// Configuration for a single OpenAI-compatible provider entry. The full set
/// of providers and the currently selected one live in <c>%APPDATA%\bb\config.json</c>.
/// </summary>
public sealed class ProviderConfig
{
    /// <summary>The provider identifier (e.g., "openai", "ollama-local").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Fully-qualified chat completions endpoint URL.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>The model name to send in the request body.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Whether to request server-sent events streaming.</summary>
    public bool Stream { get; set; } = true;

    /// <summary>Per-request timeout. Defaults to 8 seconds.</summary>
    public int TimeoutSeconds { get; set; } = 8;
}
