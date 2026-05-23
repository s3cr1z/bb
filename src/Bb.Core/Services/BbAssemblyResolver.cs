// -----------------------------------------------------------------------------
// BbAssemblyResolver.cs - bridges module/bin/*.dll into the .NET Framework
// Fusion loader on Windows PowerShell 5.1.
//
// The problem we are solving: PowerShellStandard.Library 5.1.1 is built
// against System.Text.Json 6.0.10 and System.Security.Cryptography.ProtectedData
// 8.0.0, both of which have transitive dependencies (System.Memory,
// System.Buffers, etc.) that PS 5.1 does not ship inbox. The Fusion loader
// only searches the GAC and the host process appbase - it will never look
// in module/bin/, no matter where Bb.Core.dll was loaded from.
//
// We can't add binding redirects because we can't modify powershell.exe.config.
// We can't use AssemblyLoadContext because that's PS 7+ only.
//
// Instead: register an AssemblyResolve handler that maps short-name requests
// to the assembly we already loaded from module/bin/, even if the requested
// version differs (e.g. System.Text.Json's metadata references System.Memory
// 4.0.1.1 but we ship 4.0.1.2). Strict version binding is the entire reason
// this is needed - the resolver returns whatever short-name match exists,
// version differences be damned.
//
// This file ships INSIDE Bb.Core.dll so the resolver is compiled, not
// re-interpreted on every callback. The PowerShell wrapper in bb.psm1 calls
// BbAssemblyResolver.Register(...) exactly once at module load.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Bb.Core.Services;

/// <summary>
/// Registers a process-wide AssemblyResolve handler that resolves
/// short-name requests against module/bin/. Only assemblies whose file
/// is present in the bin/ directory at registration time are eligible;
/// everything else is rejected in O(1) so we don't recurse on every
/// Pester resource-DLL probe.
/// </summary>
public static class BbAssemblyResolver
{
    private static readonly object s_lock = new();
    private static HashSet<string>? s_shippedShortNames;
    private static string? s_binPath;
    private static bool s_registered;

    /// <summary>
    /// Idempotent. Safe to call from Import-Module -Force.
    /// </summary>
    public static void Register(string binPath)
    {
        if (string.IsNullOrEmpty(binPath))
        {
            throw new ArgumentException("binPath cannot be null or empty.", nameof(binPath));
        }

        lock (s_lock)
        {
            // Build / refresh the allow-list every call so adding a new DLL
            // to module/bin/ between sessions is picked up without a restart.
            var shipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(binPath))
            {
                foreach (var path in Directory.GetFiles(binPath, "*.dll"))
                {
                    shipped.Add(Path.GetFileNameWithoutExtension(path));
                }
            }
            s_shippedShortNames = shipped;
            s_binPath           = binPath;

            if (s_registered) { return; }
            AppDomain.CurrentDomain.AssemblyResolve += OnResolve;
            s_registered = true;
        }
    }

    private static Assembly? OnResolve(object? sender, ResolveEventArgs args)
    {
        try
        {
            // AssemblyName parsing can itself trigger AssemblyResolve in
            // exotic edge cases, but on .NET Framework 4.7.2+ the type
            // is mscorlib-resident, so this is safe.
            var requestedShort = new AssemblyName(args.Name).Name;
            if (string.IsNullOrEmpty(requestedShort)) return null;
            if (s_shippedShortNames is null || !s_shippedShortNames.Contains(requestedShort)) return null;

            // First: short-name match in the AppDomain - return whatever
            // version is already loaded, ignoring the requested version.
            // This is what makes strict-binding mismatches resolvable.
            foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(loaded.GetName().Name, requestedShort, StringComparison.OrdinalIgnoreCase))
                {
                    return loaded;
                }
            }

            // Last resort: load from disk.
            if (s_binPath is not null)
            {
                var candidate = Path.Combine(s_binPath, requestedShort + ".dll");
                if (File.Exists(candidate))
                {
                    return Assembly.LoadFrom(candidate);
                }
            }
        }
        catch
        {
            // Swallow - returning null from the handler causes the loader
            // to throw FileNotFoundException as if we were never here, which
            // is the desired behavior on any failure.
        }
        return null;
    }
}
