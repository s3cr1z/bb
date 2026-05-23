function Set-BbConfig {
    <#
    .SYNOPSIS
        Configure a bb provider (endpoint, model, API key).

    .DESCRIPTION
        Writes provider details to %APPDATA%\bb\config.json and stores the
        API key encrypted with DPAPI in %APPDATA%\bb\credentials.dat. Both
        files are per-user - they can only be decrypted by the Windows
        account that wrote them.

        Calling without -ApiKey leaves an existing key untouched, so you can
        change just the endpoint or model. Calling without -Endpoint /
        -Model on a new provider defaults to OpenAI's chat completions URL
        and gpt-4o-mini.

        The first provider you configure becomes the active provider. Use
        Use-BbProvider to switch.

    .PARAMETER Provider
        Name of the provider (e.g. 'openai', 'ollama-local', 'azure-east').
        Treated case-insensitively.

    .PARAMETER ApiKey
        The API key (or any bearer token) to send as Authorization.
        Stored encrypted; never written to disk in plaintext.

    .PARAMETER Endpoint
        Chat-completions URL. Defaults to https://api.openai.com/v1/chat/completions.

    .PARAMETER Model
        Model identifier sent in the request body. Defaults to gpt-4o-mini.

    .PARAMETER Stream
        Whether to request server-sent events streaming. Defaults to $true.

    .PARAMETER TimeoutSeconds
        Per-request timeout, in seconds (1..600). Defaults to 8.

    .PARAMETER SetActive
        Mark this provider as the active one after writing it. Implicit on
        first-run.

    .EXAMPLE
        PS> Set-BbConfig -Provider openai -ApiKey sk-...

    .EXAMPLE
        PS> Set-BbConfig -Provider ollama -Endpoint http://localhost:11434/v1/chat/completions -Model llama3.2 -ApiKey ignored
    #>
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [ValidateNotNullOrEmpty()]
        [string] $Provider,

        [Parameter()]
        [string] $ApiKey,

        [Parameter()]
        [string] $Endpoint,

        [Parameter()]
        [string] $Model,

        [Parameter()]
        [Nullable[bool]] $Stream,

        [Parameter()]
        [ValidateRange(1, 600)]
        [int] $TimeoutSeconds,

        [Parameter()]
        [switch] $SetActive
    )

    if (-not $PSCmdlet.ShouldProcess("provider '$Provider'", 'Write bb configuration')) { return }

    $config = [Bb.Core.Services.BbConfigStore]::Load()
    $existing = $null
    if ($config.Providers.ContainsKey($Provider)) {
        $existing = $config.Providers[$Provider]
    }

    $entry = [Bb.Core.Models.ProviderConfig]::new()
    $entry.Name = $Provider

    if ($PSBoundParameters.ContainsKey('Endpoint') -and -not [string]::IsNullOrWhiteSpace($Endpoint)) {
        $entry.Endpoint = $Endpoint
    } elseif ($existing) {
        $entry.Endpoint = $existing.Endpoint
    } else {
        $entry.Endpoint = 'https://api.openai.com/v1/chat/completions'
    }

    if ($PSBoundParameters.ContainsKey('Model') -and -not [string]::IsNullOrWhiteSpace($Model)) {
        $entry.Model = $Model
    } elseif ($existing) {
        $entry.Model = $existing.Model
    } else {
        $entry.Model = 'gpt-4o-mini'
    }

    if ($PSBoundParameters.ContainsKey('Stream') -and $null -ne $Stream) {
        $entry.Stream = [bool]$Stream
    } elseif ($existing) {
        $entry.Stream = $existing.Stream
    } else {
        $entry.Stream = $true
    }

    if ($PSBoundParameters.ContainsKey('TimeoutSeconds')) {
        $entry.TimeoutSeconds = $TimeoutSeconds
    } elseif ($existing) {
        $entry.TimeoutSeconds = $existing.TimeoutSeconds
    } else {
        $entry.TimeoutSeconds = 8
    }

    $config.Providers[$Provider] = $entry

    # First provider configured wins active-by-default, unless the user passed -SetActive.
    if ($SetActive.IsPresent -or [string]::IsNullOrWhiteSpace($config.ActiveProvider)) {
        $config.ActiveProvider = $Provider
    }

    [Bb.Core.Services.BbConfigStore]::Save($config)

    if ($PSBoundParameters.ContainsKey('ApiKey')) {
        if ([string]::IsNullOrEmpty($ApiKey)) {
            throw 'Set-BbConfig: -ApiKey was passed empty. Omit the parameter to leave the existing key untouched.'
        }
        [Bb.Core.Services.CredentialStore]::SetSecret($Provider, $ApiKey)
    }

    [pscustomobject]@{
        Provider       = $entry.Name
        Endpoint       = $entry.Endpoint
        Model          = $entry.Model
        Stream         = $entry.Stream
        TimeoutSeconds = $entry.TimeoutSeconds
        Active         = ($config.ActiveProvider -ieq $Provider)
        HasApiKey      = [bool]([Bb.Core.Services.CredentialStore]::GetSecret($Provider))
    }
}
