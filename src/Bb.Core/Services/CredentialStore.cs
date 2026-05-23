using System;

namespace Bb.Core.Services;

/// <summary>
/// DPAPI-backed encrypted credential storage for provider API keys. Scaffold
/// stub in PR 2; full DPAPI implementation lands in PR 3.
/// </summary>
/// <remarks>
/// PR 3 will pin <c>System.Security.Cryptography.ProtectedData 8.0.0</c> and
/// load this assembly through a custom <c>AssemblyLoadContext</c> on PS 7 to
/// isolate it from PowerShell 7's inbox 6.x version.
/// </remarks>
public static class CredentialStore
{
    /// <summary>
    /// Retrieve a stored secret by provider name.
    /// </summary>
    /// <param name="providerName">The provider identifier (e.g., "openai").</param>
    /// <returns>The decrypted secret, or <c>null</c> if not present.</returns>
    public static string? GetSecret(string providerName)
    {
        // PR 3: read from %APPDATA%\bb\credentials.dat, ProtectedData.Unprotect with CurrentUser scope.
        throw new NotImplementedException("CredentialStore.GetSecret is implemented in PR 3 (MVP).");
    }

    /// <summary>
    /// Encrypt and persist a secret for a provider name.
    /// </summary>
    public static void SetSecret(string providerName, string secret)
    {
        // PR 3: ProtectedData.Protect with CurrentUser scope, write to %APPDATA%\bb\credentials.dat.
        throw new NotImplementedException("CredentialStore.SetSecret is implemented in PR 3 (MVP).");
    }
}
