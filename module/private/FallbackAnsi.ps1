function Write-BbActionBar {
    <#
    .SYNOPSIS
        Render the proposed command with risk badge and action keys.

    .DESCRIPTION
        PR 2 implementation: plain Write-Host output that works on every
        Windows PowerShell host without ANSI / VT100 assumptions. PR 3
        Milestone 3 adds:
            - Spectre.Console rendering on PS 7+ (via SpectreRenderer.ps1)
            - Rich ANSI escape codes on PS 5.1 hosts with VT support
            - Proper [E] [C] [R] [V] [Q] keypress loop

        The output here is intentionally boring — the contract test only
        cares that this function runs without error and the command field
        is reachable from Invoke-Bb.

    .PARAMETER Response
        The validated AiResponse returned by Invoke-BbAiQuery.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object] $Response
    )

    # Explicit null check: $Response.Risk is a [Bb.Core.Models.RiskLevel] enum,
    # and RiskLevel.Low = 0 is falsy under PowerShell's `if` coercion. Without
    # this explicit `-ne $null` we'd label every low-risk command as UNKNOWN.
    $risk = if ($null -ne $Response.Risk) { $Response.Risk.ToString().ToUpperInvariant() } else { 'UNKNOWN' }
    Write-Host ''
    Write-Host ('  [{0}] {1}' -f $risk, $Response.Command)
    if ($Response.Explanation) {
        Write-Host ('       {0}' -f $Response.Explanation) -ForegroundColor DarkGray
    }
    Write-Host ''
}
