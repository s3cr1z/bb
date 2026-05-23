function Get-BbConfig {
    <#
    .SYNOPSIS
        List configured bb providers.

    .DESCRIPTION
        Reads %APPDATA%\bb\config.json and emits one object per configured
        provider, marking which one is currently active. API keys are never
        included; HasApiKey only signals whether a key is present.

    .PARAMETER Provider
        If provided, returns only that provider (or nothing if not configured).

    .EXAMPLE
        PS> Get-BbConfig | Format-Table
    #>
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param(
        [Parameter(Position = 0)]
        [string] $Provider
    )

    $config = [Bb.Core.Services.BbConfigStore]::Load()
    foreach ($name in $config.Providers.Keys) {
        if ($PSBoundParameters.ContainsKey('Provider') -and $name -ine $Provider) { continue }
        $entry = $config.Providers[$name]
        [pscustomobject]@{
            Provider       = $entry.Name
            Endpoint       = $entry.Endpoint
            Model          = $entry.Model
            Stream         = $entry.Stream
            TimeoutSeconds = $entry.TimeoutSeconds
            Active         = ($config.ActiveProvider -ieq $name)
            HasApiKey      = [bool]([Bb.Core.Services.CredentialStore]::GetSecret($name))
        }
    }
}
