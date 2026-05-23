#requires -Version 5.1
<#
.SYNOPSIS
    Runs the bb test suite: PSScriptAnalyzer, dotnet test (xUnit), then Pester.

.DESCRIPTION
    Single entry point for local + CI validation. The pipeline is:

      1. PSScriptAnalyzer -Settings PSGallery on the module/ tree. This is
         the same ruleset Publish-Module enforces, so failing here means we
         couldn't ship to PSGallery anyway.
      2. dotnet test on Bb.sln (xUnit smoke tests for the C# core).
      3. Invoke-Pester on tests/Bb.Module.Tests, which includes the
         session-state contract test that is the whole reason this CLI is
         a hybrid binary module rather than a standalone executable.

    Designed to work on Windows PowerShell 5.1 (Desktop) and PowerShell 7.x
    (Core). CI runs this on both editions to catch any cross-edition drift
    in the module wiring.

.PARAMETER Configuration
    Build configuration. Defaults to Release. Forwarded to dotnet test.

.PARAMETER SkipDotNet
    Skip the dotnet test stage (useful when iterating on PowerShell code).

.PARAMETER SkipAnalyzer
    Skip the PSScriptAnalyzer stage (useful when iterating on style).

.EXAMPLE
    PS> ./scripts/test.ps1

.EXAMPLE
    PS> ./scripts/test.ps1 -SkipDotNet
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $SkipDotNet,
    [switch] $SkipAnalyzer
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path -Parent $PSScriptRoot
$solution   = Join-Path $repoRoot 'Bb.sln'
$moduleDir  = Join-Path $repoRoot 'module'
$pesterDir  = Join-Path $repoRoot 'tests/Bb.Module.Tests'

# ---------------- 1. PSScriptAnalyzer ----------------
if (-not $SkipAnalyzer) {
    Write-Host '==> PSScriptAnalyzer (PSGallery ruleset)' -ForegroundColor Cyan
    Import-Module PSScriptAnalyzer -ErrorAction Stop
    $findings = Invoke-ScriptAnalyzer -Path $moduleDir -Recurse -Settings PSGallery
    if ($findings) {
        $findings | Format-Table -AutoSize | Out-Host
        throw "PSScriptAnalyzer reported $($findings.Count) finding(s)."
    }
    Write-Host '    clean: 0 findings' -ForegroundColor Green
}

# ---------------- 2. dotnet test (xUnit) -------------
if (-not $SkipDotNet) {
    Write-Host "==> dotnet test $solution -c $Configuration" -ForegroundColor Cyan
    & dotnet test $solution -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet test exited with code $LASTEXITCODE"
    }
}

# ---------------- 3. Pester ---------------------------
Write-Host '==> Pester (module + contract suite)' -ForegroundColor Cyan
Import-Module Pester -MinimumVersion 5.0 -ErrorAction Stop

$pesterConfig = New-PesterConfiguration
$pesterConfig.Run.Path           = $pesterDir
$pesterConfig.Run.Exit           = $true
$pesterConfig.Output.Verbosity   = 'Detailed'
$pesterConfig.CodeCoverage.Enabled = $false

Invoke-Pester -Configuration $pesterConfig
