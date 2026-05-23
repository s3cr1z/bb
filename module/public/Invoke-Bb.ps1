function Invoke-Bb {
    <#
    .SYNOPSIS
        Generate and run a PowerShell command from a natural-language prompt.

    .DESCRIPTION
        Gathers session context (current directory, PowerShell edition),
        invokes the configured AI provider via Invoke-BbAiQuery, renders
        the action bar with a risk badge, blocks on a [Y/n/edit/quit]
        confirmation, and (on Y) executes the resulting command IN THE
        CALLER'S SESSION STATE via $ExecutionContext.InvokeCommand.InvokeScript($false, ...)
        so that commands like 'cd ..' or '$env:FOO = ''bar''' actually
        persist for the next prompt.

        The implementation supports two modes:

        - Default: routes through the configured provider in
          %APPDATA%\bb\config.json. Errors clearly when no provider is set.
        - -Stub: short-circuits the network and uses the deterministic echo
          response. Used by tests; also handy for offline development.

        Pass -Debug (or set $env:BB_DEBUG=1) to print a one-line timing
        summary per request to stderr. Timings stay local - bb never
        transmits telemetry.

    .PARAMETER Prompt
        The natural-language instruction. All positional arguments are
        joined with spaces, so you can type 'bb find all txt files modified
        today' without quotes.

    .PARAMETER WhatIf
        Show the generated command and skip execution.

    .PARAMETER Yes
        Skip the interactive confirmation prompt for non-high-risk commands.
        High-risk commands always require explicit confirmation regardless
        of -Yes.

    .EXAMPLE
        PS> bb show me the largest 5 files in this folder

    .EXAMPLE
        PS> bb -Stub testing the wiring
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter(Mandatory = $true, ValueFromRemainingArguments = $true, Position = 0)]
        [string[]] $Prompt,

        [Parameter()]
        [switch] $Stub,

        [Parameter()]
        [switch] $Yes,

        [Parameter()]
        [string] $Provider,

        [Parameter()]
        [ValidateRange(1, 600)]
        [int] $TimeoutSeconds
    )

    $debugTiming = $PSBoundParameters.ContainsKey('Debug') -or $env:BB_DEBUG -eq '1'
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    $joinedPrompt = ($Prompt -join ' ').Trim()
    if ([string]::IsNullOrWhiteSpace($joinedPrompt)) {
        throw 'bb: prompt cannot be empty.'
    }

    # --- Gather context (CWD + PS version) --------------------------------
    $context = Get-BbSessionContext
    $tContextMs = $stopwatch.ElapsedMilliseconds

    Write-Verbose ("bb: prompt='{0}' cwd='{1}' psversion='{2}'" -f $joinedPrompt, $context.WorkingDirectory, $context.PSVersion)

    # --- Call the binary cmdlet -------------------------------------------
    $queryArgs = @{
        UserPrompt       = $joinedPrompt
        WorkingDirectory = $context.WorkingDirectory
    }
    if ($Stub.IsPresent) { $queryArgs['Stub'] = $true }
    if ($PSBoundParameters.ContainsKey('Provider')) { $queryArgs['Provider'] = $Provider }
    if ($PSBoundParameters.ContainsKey('TimeoutSeconds')) { $queryArgs['TimeoutSeconds'] = $TimeoutSeconds }

    $tBeforeQueryMs = $stopwatch.ElapsedMilliseconds
    $response = Invoke-BbAiQuery @queryArgs
    $tQueryMs = $stopwatch.ElapsedMilliseconds - $tBeforeQueryMs

    if ($null -eq $response) {
        throw 'bb: Invoke-BbAiQuery returned no response.'
    }

    # --- Render the action bar --------------------------------------------
    Write-BbActionBar -Response $response

    # --- Decide whether to execute ----------------------------------------
    # Order of precedence:
    #   1. -WhatIf upstream -> never execute, just show.
    #   2. -Yes + non-high -> execute without prompting.
    #   3. Non-interactive host (no console) -> auto-execute low risk only.
    #   4. Otherwise -> Read-BbConfirmation prompt.
    $command = $response.Command
    $whatIfPreference = $PSBoundParameters['WhatIf'] -or $WhatIfPreference -eq $true
    $isHigh = ($null -ne $response.Risk -and $response.Risk -eq [Bb.Core.Models.RiskLevel]::High)

    $decision = if ($whatIfPreference) {
        'skip'
    } elseif ($Yes.IsPresent -and -not $isHigh) {
        'execute'
    } elseif (-not [Environment]::UserInteractive -or [Console]::IsInputRedirected) {
        if ($null -ne $response.Risk -and $response.Risk -eq [Bb.Core.Models.RiskLevel]::Low) { 'execute' } else { 'skip' }
    } else {
        Read-BbConfirmation -Response $response
    }

    if ($decision -like 'edit:*') {
        $command = $decision.Substring(5)
        $decision = 'execute'
    }

    $tDecisionMs = $stopwatch.ElapsedMilliseconds - $tBeforeQueryMs - $tQueryMs

    # --- Execute in the CALLER's session state ----------------------------
    # The literal $false here is what makes 'cd ..' actually change $PWD in
    # the user's prompt - see the contract test in
    # tests/Bb.Module.Tests/SessionPersistence.Tests.ps1
    if ($decision -eq 'execute') {
        if ($PSCmdlet.ShouldProcess($command, 'Execute generated PowerShell command')) {
            $script = $ExecutionContext.InvokeCommand.NewScriptBlock($command)
            $ExecutionContext.InvokeCommand.InvokeScript($false, $script, $null, $null)
        }
    } elseif ($decision -eq 'cancel') {
        Write-Host '  (cancelled)' -ForegroundColor DarkGray
    } else {
        Write-Host '  (skipped)' -ForegroundColor DarkGray
    }

    $stopwatch.Stop()
    if ($debugTiming) {
        # Local-only telemetry. NEVER persisted, never transmitted.
        $totalMs = $stopwatch.ElapsedMilliseconds
        Write-Host ("  [bb --debug] context={0}ms query={1}ms decision={2}ms total={3}ms" -f `
            $tContextMs, $tQueryMs, $tDecisionMs, $totalMs) -ForegroundColor DarkCyan
    }
}
