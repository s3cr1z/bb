# ---------------------------------------------------------------------------
# bb.psm1 - script wrapper for the bb module
#
# Loads private helpers, then public functions, then sets the 'bb' alias.
# Cross-edition: must import cleanly on Windows PowerShell 5.1 (Desktop) AND
# PowerShell 7.4+ (Core). All edition-specific behavior is gated inside the
# private/ scripts, not here.
# ---------------------------------------------------------------------------

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:BbModuleRoot = $PSScriptRoot
$script:BbBinPath    = Join-Path -Path $script:BbModuleRoot -ChildPath 'bin'

# --- Module-private assembly resolver (Windows PowerShell 5.1) ------------
# System.Text.Json 6.x and System.Security.Cryptography.ProtectedData 8.x
# both have transitive deps (System.Memory, System.Buffers, etc.) that PS 5.1
# does not ship in its private bin directory and that the Fusion loader
# cannot find via NestedModules' load path. We register a per-AppDomain
# resolver pointing at our module/bin/ so dependencies resolve at runtime.
# On PS 7+ the default AssemblyLoadContext handles this; the handler is a
# no-op there since the resolver only fires on misses.
# Eagerly pre-load every dependency DLL from module/bin/ so all of
# System.Text.Json's transitive deps live in the AppDomain before any cmdlet
# call. .NET Framework's Fusion loader does not search module/bin/ on its
# own, so we have to either ship a binding redirect (we can't - we don't
# own powershell.exe.config) or do this manually.
if (Test-Path -LiteralPath $script:BbBinPath) {
    foreach ($dll in (Get-ChildItem -LiteralPath $script:BbBinPath -Filter '*.dll' -File -ErrorAction Ignore)) {
        if ($dll.Name -ieq 'Bb.Core.dll') { continue }
        try { [System.Reflection.Assembly]::LoadFrom($dll.FullName) | Out-Null }
        catch { Write-Verbose ("bb.psm1: pre-load of '{0}' skipped: {1}" -f $dll.Name, $_.Exception.Message) }
    }
}

# Belt-and-suspenders: register a compiled AssemblyResolve handler so that
# any straggling transitive deps (e.g. System.Memory 4.0.1.1 referenced in
# System.Text.Json's metadata when we ship 4.0.1.2) still resolve. The
# resolver is in C# (BbAssemblyResolver) for performance and to avoid
# PowerShell-scriptblock stack costs that would otherwise add up across
# the hundreds of probes Pester triggers during test discovery.
# NestedModules in bb.psd1 has already loaded Bb.Core.dll by this point.
try {
    [Bb.Core.Services.BbAssemblyResolver]::Register($script:BbBinPath)
} catch {
    Write-Warning ("bb.psm1: failed to register assembly resolver: {0}" -f $_.Exception.Message)
}

# --- Dot-source helpers in dependency order --------------------------------
$privateScripts = @(
    'ContextGatherer.ps1'
    'FallbackAnsi.ps1'
    'SpectreRenderer.ps1'
    'AlcLoader.ps1'
)
foreach ($name in $privateScripts) {
    $path = Join-Path -Path $script:BbModuleRoot -ChildPath "private/$name"
    if (Test-Path -LiteralPath $path) {
        . $path
    }
}

$publicScripts = @(
    'Invoke-Bb.ps1'
    'Set-BbConfig.ps1'
    'Use-BbProvider.ps1'
    'Get-BbConfig.ps1'
    'Remove-BbProvider.ps1'
)
foreach ($name in $publicScripts) {
    $path = Join-Path -Path $script:BbModuleRoot -ChildPath "public/$name"
    if (Test-Path -LiteralPath $path) {
        . $path
    } else {
        throw "bb.psm1: missing required public script '$name'."
    }
}

# --- PS-7-only assembly isolation ------------------------------------------
# On PS 7, load Bb.Core.dll through a custom AssemblyLoadContext to isolate
# the DPAPI assembly (System.Security.Cryptography.ProtectedData 8.0.0,
# pinned in PR 3) from PowerShell 7's inbox 6.x version. On PS 5.1 the
# inbox-collision problem does not exist, so we let NestedModules do the
# loading via the manifest.
#
# PR 2 stub: the loader is a no-op until PR 3 wires it up. We still call it
# so the wiring is verifiable and the call site doesn't have to grow later.
if ($PSVersionTable.PSVersion.Major -ge 7) {
    if (Get-Command -Name 'Initialize-BbAssemblyLoadContext' -ErrorAction Ignore) {
        Initialize-BbAssemblyLoadContext -ModuleRoot $script:BbModuleRoot
    }
}

# --- Alias -----------------------------------------------------------------
# 'bb' is the user-facing entry point. Export is declared in bb.psd1.
Set-Alias -Name 'bb' -Value 'Invoke-Bb' -Scope Script
