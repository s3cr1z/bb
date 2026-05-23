function Invoke-Bb {
    <#
    .SYNOPSIS
        Generate and run a PowerShell command from a natural-language prompt.

    .DESCRIPTION
        Gathers session context (current directory, PowerShell edition),
        invokes the AI provider (or the deterministic stub in PR 2), and
        executes the resulting command IN THE CALLER'S SESSION STATE via
        $ExecutionContext.InvokeCommand.InvokeScript($false, ...) so that
        commands like 'cd ..' or '$env:FOO = ''bar''' actually persist for
        the next prompt.

    .PARAMETER Prompt
        The natural-language instruction. All positional arguments are
        joined with spaces, so you can type 'bb find all txt files modified
        today' without quotes.

    .PARAMETER WhatIf
        Show the generated command without executing it. Useful for the
        scaffold smoke test.

    .EXAMPLE
        PS> bb show me the largest 5 files in this folder

    .NOTES
        PR 2 scaffold: the Invoke-BbAiQuery cmdlet is a deterministic echo
        stub. PR 3 replaces it with the real AI call, JSON-schema validation,
        and safety classifier chain.
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter(Mandatory = $true, ValueFromRemainingArguments = $true, Position = 0)]
        [string[]] $Prompt
    )

    $joinedPrompt = ($Prompt -join ' ').Trim()
    if ([string]::IsNullOrWhiteSpace($joinedPrompt)) {
        throw 'bb: prompt cannot be empty.'
    }

    # --- Gather context (CWD + PS version) --------------------------------
    $context = Get-BbSessionContext

    Write-Verbose ("bb: prompt='{0}' cwd='{1}' psversion='{2}'" -f $joinedPrompt, $context.WorkingDirectory, $context.PSVersion)

    # --- Call the binary cmdlet (PR 2: deterministic stub) ----------------
    # PR 3 will pass a configured ProviderConfig + system prompt + cancellation
    # token. PR 2 uses the -Stub switch path explicitly so the contract is
    # exercised even with no provider configured.
    $response = Invoke-BbAiQuery -UserPrompt $joinedPrompt -WorkingDirectory $context.WorkingDirectory -Stub

    if ($null -eq $response) {
        throw 'bb: Invoke-BbAiQuery returned no response.'
    }

    # --- Render (PR 2: plain text; PR 3: action bar TUI) ------------------
    Write-BbActionBar -Response $response

    # --- Execute in the CALLER's session state ----------------------------
    # The literal $false here is what makes 'cd ..' actually change $PWD in
    # the user's prompt — see the contract test in
    # tests/Bb.Module.Tests/SessionPersistence.Tests.ps1
    if ($PSCmdlet.ShouldProcess($response.Command, 'Execute generated PowerShell command')) {
        $script = $ExecutionContext.InvokeCommand.NewScriptBlock($response.Command)
        $ExecutionContext.InvokeCommand.InvokeScript($false, $script, $null, $null)
    }
}
