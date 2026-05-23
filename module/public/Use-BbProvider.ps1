function Use-BbProvider {
    <#
    .SYNOPSIS
        Switch the active provider for subsequent bb invocations.

    .DESCRIPTION
        Updates the `activeProvider` field in %APPDATA%\bb\config.json. The
        provider must already be configured via Set-BbConfig - this function
        only switches between known providers, it does not create them.

    .PARAMETER Name
        The provider name. Case-insensitive.

    .EXAMPLE
        PS> Use-BbProvider ollama-local
    #>
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Low')]
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [ValidateNotNullOrEmpty()]
        [ArgumentCompleter({
            param($commandName, $parameterName, $wordToComplete, $commandAst, $fakeBoundParameter)
            try {
                $cfg = [Bb.Core.Services.BbConfigStore]::Load()
                foreach ($key in $cfg.Providers.Keys) {
                    if ($key -like "$wordToComplete*") {
                        [System.Management.Automation.CompletionResult]::new($key, $key, 'ParameterValue', $key)
                    }
                }
            } catch {
                # Tab completion must never throw - silently no-op on any
                # failure (e.g. config file unreadable from a fresh shell).
                Write-Debug ("Use-BbProvider completer suppressed error: {0}" -f $_.Exception.Message)
            }
        })]
        [string] $Name
    )

    if (-not $PSCmdlet.ShouldProcess("active provider", "Switch to '$Name'")) { return }

    $config = [Bb.Core.Services.BbConfigStore]::Load()
    if (-not $config.Providers.ContainsKey($Name)) {
        $known = if ($config.Providers.Count -gt 0) {
            ($config.Providers.Keys | Sort-Object) -join ', '
        } else {
            '(none - run Set-BbConfig first)'
        }
        throw "Use-BbProvider: provider '$Name' is not configured. Known providers: $known"
    }

    $config.ActiveProvider = $Name
    [Bb.Core.Services.BbConfigStore]::Save($config)

    $entry = $config.Providers[$Name]
    [pscustomobject]@{
        Provider       = $entry.Name
        Endpoint       = $entry.Endpoint
        Model          = $entry.Model
        Stream         = $entry.Stream
        TimeoutSeconds = $entry.TimeoutSeconds
        Active         = $true
        HasApiKey      = [bool]([Bb.Core.Services.CredentialStore]::GetSecret($Name))
    }
}
