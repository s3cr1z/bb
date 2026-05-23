function Remove-BbProvider {
    <#
    .SYNOPSIS
        Remove a configured bb provider and its stored API key.

    .DESCRIPTION
        Deletes the provider from %APPDATA%\bb\config.json and erases its
        encrypted API key from credentials.dat. If the removed provider was
        active, the active pointer is cleared - set a new active via
        Use-BbProvider before the next bb invocation.

    .PARAMETER Name
        The provider name. Case-insensitive.

    .EXAMPLE
        PS> Remove-BbProvider azure-east
    #>
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [ValidateNotNullOrEmpty()]
        [string] $Name
    )

    if (-not $PSCmdlet.ShouldProcess("provider '$Name'", 'Remove from bb configuration (also erases stored API key)')) { return }

    $config = [Bb.Core.Services.BbConfigStore]::Load()
    $existed = $config.Providers.Remove($Name)

    if (-not $existed) {
        Write-Warning ("Remove-BbProvider: provider '{0}' was not configured; nothing to remove." -f $Name)
        return
    }

    if ($config.ActiveProvider -ieq $Name) {
        $config.ActiveProvider = $null
    }

    [Bb.Core.Services.BbConfigStore]::Save($config)
    [Bb.Core.Services.CredentialStore]::RemoveSecret($Name)
}
