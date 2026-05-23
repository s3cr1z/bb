using System;
using System.IO;
using System.Text.Json;
using Bb.Core.Models;

namespace Bb.Core.Services;

/// <summary>
/// Reads and writes <c>%APPDATA%\bb\config.json</c>. The file holds provider
/// endpoints, model names, and the currently active provider — never API
/// keys (those go to <see cref="CredentialStore"/>).
/// </summary>
public static class BbConfigStore
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Load the current config from disk. Returns an empty config (no
    /// providers, no active) if the file does not exist, so callers don't
    /// have to special-case first-run.
    /// </summary>
    public static BbConfig Load()
    {
        var path = BbPaths.GetConfigPath();
        if (!File.Exists(path))
        {
            return new BbConfig();
        }

        try
        {
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<BbConfig>(json, s_jsonOptions) ?? new BbConfig();
            // System.Text.Json deserializes Dictionary<string,T> with the default
            // equality comparer (case-sensitive), losing the OrdinalIgnoreCase
            // we set on construction. Rebuild it so provider lookups stay
            // case-insensitive across save/load roundtrips.
            loaded.Providers = new System.Collections.Generic.Dictionary<string, ProviderConfig>(
                loaded.Providers, StringComparer.OrdinalIgnoreCase);
            return loaded;
        }
        catch (JsonException ex)
        {
            // Corrupt config — fail loud so the user knows. We don't try to
            // "recover" by overwriting because the file might contain hand-edited
            // provider URLs they care about.
            throw new InvalidDataException(
                $"bb: config.json at '{path}' is not valid JSON. Fix or delete it, then run 'bb config'. " +
                $"Underlying error: {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Persist the config to disk. Writes atomically via a temp file + rename
    /// so a crash in the middle of writing doesn't leave a half-written file.
    /// </summary>
    public static void Save(BbConfig config)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));

        var finalPath = BbPaths.GetConfigPath();
        var tempPath = finalPath + ".tmp";

        var json = JsonSerializer.Serialize(config, s_jsonOptions);
        File.WriteAllText(tempPath, json);

        // File.Move with overwrite is netstandard2.1+. On netstandard2.0 we
        // delete-then-move, which is good enough because we never read from
        // the temp path elsewhere.
        if (File.Exists(finalPath)) File.Delete(finalPath);
        File.Move(tempPath, finalPath);
    }
}
