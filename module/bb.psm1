# ---------------------------------------------------------------------------
# bb.psm1 — script wrapper for the bb module
#
# Loads private helpers, then public functions, then sets the 'bb' alias.
# Cross-edition: must import cleanly on Windows PowerShell 5.1 (Desktop) AND
# PowerShell 7.4+ (Core). All edition-specific behavior is gated inside the
# private/ scripts, not here.
# ---------------------------------------------------------------------------

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:BbModuleRoot = $PSScriptRoot

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
