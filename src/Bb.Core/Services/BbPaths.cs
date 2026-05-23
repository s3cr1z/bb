using System;
using System.IO;

namespace Bb.Core.Services;

/// <summary>
/// Resolves on-disk paths used by the bb CLI. Centralized so tests can override
/// the root via the <c>BB_HOME</c> environment variable and assert against
/// known locations without polluting the user's real <c>%APPDATA%\bb\</c>.
/// </summary>
public static class BbPaths
{
    /// <summary>
    /// Root directory for bb configuration + credentials.
    /// </summary>
    /// <remarks>
    /// Precedence: <c>$env:BB_HOME</c> (used by tests) wins, otherwise
    /// <c>%APPDATA%\bb</c> (Windows convention). Creates the directory on
    /// demand so callers don't have to.
    /// </remarks>
    public static string GetRoot()
    {
        var explicitRoot = Environment.GetEnvironmentVariable("BB_HOME");
        var root = !string.IsNullOrWhiteSpace(explicitRoot)
            ? explicitRoot!
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "bb");

        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>Full path to <c>config.json</c>.</summary>
    public static string GetConfigPath() => Path.Combine(GetRoot(), "config.json");

    /// <summary>Full path to <c>credentials.dat</c>.</summary>
    public static string GetCredentialsPath() => Path.Combine(GetRoot(), "credentials.dat");
}
