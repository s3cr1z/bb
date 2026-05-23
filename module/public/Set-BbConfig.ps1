function Set-BbConfig {
    <#
    .SYNOPSIS
        Configure bb (provider endpoints, API keys, safety mode).

    .DESCRIPTION
        Scaffold stub in PR 2. PR 3 implements the real provider-configuration
        wizard, writes to %APPDATA%\bb\config.json, and persists encrypted
        API keys to credentials.dat via the DPAPI-backed CredentialStore in
        the C# core.

    .EXAMPLE
        PS> Set-BbConfig -Provider openai -ApiKey sk-...
    #>
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
    param(
        [Parameter()]
        [string] $Provider,

        [Parameter()]
        [string] $ApiKey
    )

    if (-not $PSCmdlet.ShouldProcess('bb configuration', 'Update')) { return }
    throw [System.NotImplementedException]::new('Set-BbConfig is implemented in PR 3 (MVP).')
}
