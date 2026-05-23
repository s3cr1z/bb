using System.Collections.Generic;

namespace Bb.Core.Models;

/// <summary>
/// Top-level shape of <c>%APPDATA%\bb\config.json</c>. Contains the set of
/// configured providers and a pointer to the currently active one. API keys
/// are NOT stored here — they live encrypted in <c>credentials.dat</c> alongside.
/// </summary>
public sealed class BbConfig
{
    /// <summary>Name of the currently active provider. Must match a key in <see cref="Providers"/>.</summary>
    public string? ActiveProvider { get; set; }

    /// <summary>All configured providers keyed by name.</summary>
    public Dictionary<string, ProviderConfig> Providers { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);
}
