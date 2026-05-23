function Use-BbProvider {
    <#
    .SYNOPSIS
        Switch the active provider for subsequent bb invocations.

    .DESCRIPTION
        Scaffold stub in PR 2. PR 3 implements the actual provider swap,
        persisting the choice to %APPDATA%\bb\config.json.

    .EXAMPLE
        PS> Use-BbProvider ollama-local
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [string] $Name
    )

    throw [System.NotImplementedException]::new('Use-BbProvider is implemented in PR 3 (MVP).')
}
