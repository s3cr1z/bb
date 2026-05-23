#requires -Version 5.1
<#
.SYNOPSIS
    Builds the bb solution and stages Bb.Core.dll into module/bin/.

.DESCRIPTION
    Wraps `dotnet restore` + `dotnet build` so contributors and CI have a
    single entry point. The post-build target in src/Bb.Core/Bb.Core.csproj
    copies the produced DLL into module/bin/Bb.Core.dll, which is what the
    bb.psd1 NestedModules entry expects.

    Designed to work identically on Windows PowerShell 5.1 (Desktop) and
    PowerShell 7.x (Core) so the same script runs locally and in CI.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER NoRestore
    Skip the implicit restore step. Useful in CI when restore has already
    been cached as a separate step.

.EXAMPLE
    PS> ./scripts/build.ps1

.EXAMPLE
    PS> ./scripts/build.ps1 -Configuration Debug
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [switch] $NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'Bb.sln'

if (-not (Test-Path -LiteralPath $solution)) {
    throw "Solution not found at $solution. Run from the repository root."
}

Write-Host "==> dotnet build $solution -c $Configuration" -ForegroundColor Cyan

$buildArgs = @('build', $solution, '-c', $Configuration)
if ($NoRestore) {
    $buildArgs += '--no-restore'
}

& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build exited with code $LASTEXITCODE"
}

$stagedDll = Join-Path $repoRoot 'module/bin/Bb.Core.dll'
if (-not (Test-Path -LiteralPath $stagedDll)) {
    throw "Build succeeded but Bb.Core.dll was not staged at $stagedDll"
}

Write-Host "==> staged $stagedDll" -ForegroundColor Green
