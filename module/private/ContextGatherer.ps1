function Get-BbSessionContext {
    <#
    .SYNOPSIS
        Collect the minimal session context to enrich the LLM prompt.

    .DESCRIPTION
        Returns CWD and PowerShell version. PR 3 extends this with optional
        last-error capture (truncated to 200 chars) and OS version. Context
        is strictly scoped to prevent leaking local secrets — environment
        variables, file contents, and command history are never included.
    #>
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param()

    [pscustomobject]@{
        WorkingDirectory = (Get-Location).Path
        PSVersion        = $PSVersionTable.PSVersion.ToString()
        PSEdition        = if ($PSVersionTable.PSVersion.Major -ge 6) { 'Core' } else { 'Desktop' }
    }
}
