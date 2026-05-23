function Write-BbActionBar {
    <#
    .SYNOPSIS
        Render the proposed command with risk badge and action keys.

    .DESCRIPTION
        Uses Write-Host with optional foreground colors - works on every
        PowerShell host without VT/ANSI assumptions, including the legacy
        Windows PowerShell ISE and the conhost on Windows Server. Color is
        suppressed automatically if $Host.UI.RawUI.ForegroundColor is not
        writable (e.g. when redirected to a file).

        SpectreRenderer.ps1 (PS 7+ only) can replace this renderer in the
        future; for now everyone gets the same clear output.

    .PARAMETER Response
        The validated AiResponse returned by Invoke-BbAiQuery.

    .PARAMETER NoColor
        Force monochrome output. Used by Pester so test transcripts diff cleanly.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [object] $Response,

        [Parameter()]
        [switch] $NoColor
    )

    # Explicit null check: $Response.Risk is a [Bb.Core.Models.RiskLevel] enum,
    # and RiskLevel.Low = 0 is falsy under PowerShell's `if` coercion. Without
    # this explicit `-ne $null` we'd label every low-risk command as UNKNOWN.
    $risk = if ($null -ne $Response.Risk) { $Response.Risk.ToString().ToUpperInvariant() } else { 'UNKNOWN' }

    $riskColor = switch ($risk) {
        'LOW'    { 'Green' }
        'MEDIUM' { 'Yellow' }
        'HIGH'   { 'Red' }
        default  { 'Gray' }
    }

    $adminBadge = if ($Response.RequiresAdmin) { ' [ADMIN]' } else { '' }

    Write-Host ''
    if ($NoColor) {
        Write-Host ('  [{0}]{1} {2}' -f $risk, $adminBadge, $Response.Command)
    } else {
        Write-Host '  [' -NoNewline
        Write-Host $risk -NoNewline -ForegroundColor $riskColor
        Write-Host ']' -NoNewline
        if ($adminBadge) {
            Write-Host $adminBadge -NoNewline -ForegroundColor Magenta
        }
        Write-Host (' {0}' -f $Response.Command)
    }
    if ($Response.Explanation) {
        if ($NoColor) {
            Write-Host ('       {0}' -f $Response.Explanation)
        } else {
            Write-Host ('       {0}' -f $Response.Explanation) -ForegroundColor DarkGray
        }
    }
    Write-Host ''
}

function Read-BbConfirmation {
    <#
    .SYNOPSIS
        Block on the user for [Y/n/edit/quit] after the action bar renders.

    .DESCRIPTION
        Returns one of: 'execute', 'skip', 'edit:<new command>', 'cancel'.

        - 'y' / Enter   -> execute as-is
        - 'n' / 'no'    -> skip (no execution)
        - 'e' / 'edit'  -> prompt for a replacement command, return edit:<text>
        - 'q' / 'quit' / Ctrl-C -> cancel

        High-risk commands default to 'n' rather than 'y' so a stray Enter
        does not delete files. Medium / low default to 'y'.

    .PARAMETER Response
        The AiResponse. Used to set the default per risk level.

    .PARAMETER NonInteractive
        If set (no console available, redirected stdin, or -WhatIf upstream),
        skip the prompt and return 'execute' for low risk only, otherwise 'skip'.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory = $true)]
        [object] $Response,

        [Parameter()]
        [switch] $NonInteractive
    )

    if ($NonInteractive) {
        if ($null -ne $Response.Risk -and $Response.Risk -eq [Bb.Core.Models.RiskLevel]::Low) {
            return 'execute'
        }
        return 'skip'
    }

    $isHigh = ($null -ne $Response.Risk -and $Response.Risk -eq [Bb.Core.Models.RiskLevel]::High)
    $defaultHint = if ($isHigh) { '[y/N/e/q]' } else { '[Y/n/e/q]' }

    while ($true) {
        $raw = Read-Host -Prompt ("Run? {0}" -f $defaultHint)
        $choice = if ([string]::IsNullOrWhiteSpace($raw)) {
            if ($isHigh) { 'n' } else { 'y' }
        } else {
            $raw.Trim().ToLowerInvariant()
        }

        switch -Regex ($choice) {
            '^(y|yes)$'   { return 'execute' }
            '^(n|no)$'    { return 'skip' }
            '^(q|quit|x|exit)$' { return 'cancel' }
            '^(e|edit)$'  {
                $edited = Read-Host -Prompt 'Edit command'
                if ([string]::IsNullOrWhiteSpace($edited)) {
                    Write-Host '  (no change; pick another option)' -ForegroundColor DarkGray
                    continue
                }
                return ('edit:{0}' -f $edited)
            }
            default {
                Write-Host "  (unrecognised - type y, n, e, or q)" -ForegroundColor DarkGray
            }
        }
    }
}
