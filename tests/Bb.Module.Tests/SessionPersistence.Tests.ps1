# -----------------------------------------------------------------------------
# SessionPersistence.Tests.ps1 — the contract test for the implementation-path
# decision in PRD §4 and Technical Design §3.3.
#
# Property under test: when bb generates a 'Set-Location ..' command and the
# user presses [E]xecute, the CALLER'S $PWD must actually change. If this
# test ever regresses, the entire build fails — bb is not bb without
# in-session state mutation.
#
# We mock Invoke-BbAiQuery via Pester's -ModuleName so the override is
# installed inside the bb module's session state (where Invoke-Bb resolves
# its calls), not the test runner's scope.
# -----------------------------------------------------------------------------

BeforeAll {
    $script:RepoRoot      = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $script:ManifestPath  = Join-Path $script:RepoRoot 'module/bb.psd1'

    if (-not (Test-Path -LiteralPath $script:ManifestPath)) {
        throw "bb manifest not found at $script:ManifestPath. Did you run 'dotnet build src/Bb.Core' first to stage the DLL into module/bin/?"
    }

    Import-Module $script:ManifestPath -Force -ErrorAction Stop
}

AfterAll {
    Remove-Module bb -Force -ErrorAction Ignore
}

Describe 'bb session-state persistence (the contract test)' {

    BeforeAll {
        # Replace the interactive [Y/n/e/q] prompt for the duration of the
        # describe block. Without this, every test hangs waiting on stdin.
        # The mock lives inside the bb module's session state so Invoke-Bb's
        # call site resolves to this override.
        Mock -ModuleName bb Read-BbConfirmation { 'execute' }
    }

    BeforeEach {
        # Snapshot $PWD so we can restore it regardless of what each It block does
        $script:OriginalLocation = (Get-Location).Path
    }

    AfterEach {
        Set-Location $script:OriginalLocation
        Remove-Variable -Name bbContractGlobal -Scope Global -ErrorAction Ignore
    }

    It "changes the caller's `$PWD when bb generates 'Set-Location ..'" {
        Mock -ModuleName bb Invoke-BbAiQuery {
            [PSCustomObject]@{
                Command       = 'Set-Location ..'
                Explanation   = 'go up one level'
                Risk          = [Bb.Core.Models.RiskLevel]::Low
                RequiresAdmin = $false
            }
        }

        $before = (Get-Location).Path
        $expectedAfter = Split-Path $before -Parent

        # If $PWD is already the filesystem root, this assertion is meaningless.
        # Skip in that edge case rather than producing a noisy false positive.
        if ([string]::IsNullOrEmpty($expectedAfter)) {
            Set-ItResult -Skipped -Because "test executed from a filesystem root ($before); cd .. has no effect"
            return
        }

        Invoke-Bb -Yes 'go up one directory'
        (Get-Location).Path | Should -Be $expectedAfter
        (Get-Location).Path | Should -Not -Be $before
    }

    It "propagates `$global: variables set by the generated command" {
        Mock -ModuleName bb Invoke-BbAiQuery {
            [PSCustomObject]@{
                Command       = '$global:bbContractGlobal = 42'
                Explanation   = 'set a global var'
                Risk          = [Bb.Core.Models.RiskLevel]::Low
                RequiresAdmin = $false
            }
        }

        Remove-Variable -Name bbContractGlobal -Scope Global -ErrorAction Ignore
        $global:bbContractGlobal = $null

        Invoke-Bb -Yes 'set the global flag'

        $global:bbContractGlobal | Should -Be 42
    }

    It "invokes Invoke-BbAiQuery with the joined prompt" {
        Mock -ModuleName bb Invoke-BbAiQuery {
            [PSCustomObject]@{
                Command       = 'Write-Output ok'
                Explanation   = 'noop'
                Risk          = [Bb.Core.Models.RiskLevel]::Low
                RequiresAdmin = $false
            }
        }

        Invoke-Bb -Yes find all txt files modified today | Out-Null

        Should -Invoke -ModuleName bb -CommandName Invoke-BbAiQuery -Times 1 -ParameterFilter {
            $UserPrompt -eq 'find all txt files modified today'
        }
    }

    It "throws when the prompt is empty or whitespace" {
        # Use Pester's `Should -Throw` rather than catching manually so the
        # error category and message are surfaced in the test output.
        { Invoke-Bb -Yes '   ' } | Should -Throw -ExpectedMessage '*prompt cannot be empty*'
    }
}
