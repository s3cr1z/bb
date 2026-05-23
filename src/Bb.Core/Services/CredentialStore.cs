using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Bb.Core.Services;

/// <summary>
/// DPAPI-backed encrypted credential storage for provider API keys.
/// </summary>
/// <remarks>
/// Each provider's secret is encrypted with <see cref="ProtectedData.Protect"/>
/// at <see cref="DataProtectionScope.CurrentUser"/>, base64-encoded, and stored
/// in a single JSON envelope at <c>%APPDATA%\bb\credentials.dat</c>. Only the
/// user who wrote a secret on this machine can decrypt it — copying the file
/// to another box or another user account renders it unrecoverable. There is
/// no master password to leak.
///
/// Additional entropy is mixed in via a static byte sequence so a future bb
/// release can rotate it (or add per-secret entropy) without breaking the
/// on-disk format.
/// </remarks>
public static class CredentialStore
{
    // Static entropy — NOT a secret. DPAPI mixes this into the key derivation
    // alongside the user's Windows credentials. Changing this value invalidates
    // every previously-stored secret, so don't.
    private static readonly byte[] s_entropy = Encoding.UTF8.GetBytes("bb-cli/v1/dpapi-entropy");

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Get a stored secret by provider name, or <c>null</c> if not set.</summary>
    public static string? GetSecret(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName)) throw new ArgumentException("providerName is required.", nameof(providerName));

        var envelope = LoadEnvelope();
        if (!envelope.TryGetValue(providerName, out var encryptedBase64) || string.IsNullOrEmpty(encryptedBase64))
        {
            return null;
        }

        try
        {
            var encrypted = Convert.FromBase64String(encryptedBase64);
            var plaintextBytes = ProtectedData.Unprotect(encrypted, s_entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        catch (CryptographicException ex)
        {
            // Most common cause: secret was set by a different Windows user or
            // on a different machine. Surface clearly so the user knows to
            // re-run 'bb config'.
            throw new InvalidOperationException(
                $"bb: failed to decrypt the stored API key for provider '{providerName}'. " +
                "This usually means the key was stored by a different Windows user or on a different machine. " +
                "Run 'Set-BbConfig' to store the key again. Underlying error: " + ex.Message,
                ex);
        }
    }

    /// <summary>Encrypt and persist a secret for a provider name.</summary>
    public static void SetSecret(string providerName, string secret)
    {
        if (string.IsNullOrWhiteSpace(providerName)) throw new ArgumentException("providerName is required.", nameof(providerName));
        if (secret is null) throw new ArgumentNullException(nameof(secret));

        var plaintextBytes = Encoding.UTF8.GetBytes(secret);
        var encrypted = ProtectedData.Protect(plaintextBytes, s_entropy, DataProtectionScope.CurrentUser);
        var encryptedBase64 = Convert.ToBase64String(encrypted);

        var envelope = LoadEnvelope();
        envelope[providerName] = encryptedBase64;
        SaveEnvelope(envelope);

        // Zero out the plaintext buffer — best-effort, since GC might have
        // already copied it. The real defense is DPAPI.
        Array.Clear(plaintextBytes, 0, plaintextBytes.Length);
    }

    /// <summary>Remove a stored secret. No-op if not present.</summary>
    public static void RemoveSecret(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName)) throw new ArgumentException("providerName is required.", nameof(providerName));

        var envelope = LoadEnvelope();
        if (envelope.Remove(providerName))
        {
            SaveEnvelope(envelope);
        }
    }

    private static Dictionary<string, string> LoadEnvelope()
    {
        var path = BbPaths.GetCredentialsPath();
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json, s_jsonOptions);
            return loaded is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"bb: credentials.dat at '{path}' is not valid JSON. The file may be corrupted; delete it and rerun 'Set-BbConfig'. Underlying error: {ex.Message}",
                ex);
        }
    }

    private static void SaveEnvelope(Dictionary<string, string> envelope)
    {
        var finalPath = BbPaths.GetCredentialsPath();
        var tempPath = finalPath + ".tmp";
        var json = JsonSerializer.Serialize(envelope, s_jsonOptions);
        File.WriteAllText(tempPath, json);
        if (File.Exists(finalPath)) File.Delete(finalPath);
        File.Move(tempPath, finalPath);
    }
}
