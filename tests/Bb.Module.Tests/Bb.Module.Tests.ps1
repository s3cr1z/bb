# -----------------------------------------------------------------------------
# Bb.Module.Tests.ps1 — manifest + scaffold sanity tests.
#
# Verifies the module is structurally healthy. These tests stay green even
# when scaffold stubs throw NotImplementedException — they exercise wiring
# (imports, exports, manifest validity), not behavior. The behavioral
# contract lives in SessionPersistence.Tests.ps1.
# -----------------------------------------------------------------------------

BeforeAll {
    $script:RepoRoot     = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $script:ManifestPath = Join-Path $script:RepoRoot 'module/bb.psd1'
    $script:DllPath      = Join-Path $script:RepoRoot 'module/bin/Bb.Core.dll'

    Import-Module $script:ManifestPath -Force -ErrorAction Stop
    $script:Manifest = Test-ModuleManifest -Path $script:ManifestPath -ErrorAction Stop
}

AfterAll {
    Remove-Module bb -Force -ErrorAction Ignore
}

Describe 'bb manifest' {

    It 'declares CompatiblePSEditions for both Desktop and Core' {
        $script:Manifest.CompatiblePSEditions | Should -Contain 'Desktop'
        $script:Manifest.CompatiblePSEditions | Should -Contain 'Core'
    }

    It 'requires PowerShell 5.1 as the floor' {
        $script:Manifest.PowerShellVersion | Should -Be ([Version]'5.1')
    }

    It 'declares NestedModules pointing at bin/Bb.Core.dll' {
        $script:Manifest.NestedModules | Should -Not -BeNullOrEmpty
        $nestedNames = $script:Manifest.NestedModules.Name
        $nestedNames | Should -Contain 'Bb.Core'
    }

    It 'lists explicit function exports (no wildcards)' {
        # Wildcards would block PSGallery analysis at publish time.
        $script:Manifest.ExportedFunctions.Keys | Should -Contain 'Invoke-Bb'
        $script:Manifest.ExportedFunctions.Keys | Should -Contain 'Set-BbConfig'
        $script:Manifest.ExportedFunctions.Keys | Should -Contain 'Use-BbProvider'
    }

    It 'lists explicit cmdlet exports (no wildcards)' {
        $script:Manifest.ExportedCmdlets.Keys | Should -Contain 'Invoke-BbAiQuery'
    }

    It "exports the 'bb' alias" {
        $script:Manifest.ExportedAliases.Keys | Should -Contain 'bb'
    }

    It 'does not list PwshSpectreConsole as a RequiredModule (would break 5.1)' {
        $required = @($script:Manifest.RequiredModules)
        ($required | Where-Object { $_.Name -eq 'PwshSpectreConsole' }) | Should -BeNullOrEmpty
    }
}

Describe 'bb scaffold sanity' {

    It 'staged Bb.Core.dll into module/bin/' {
        Test-Path -LiteralPath $script:DllPath | Should -BeTrue
    }

    It 'imports without error on this edition' {
        Get-Module bb | Should -Not -BeNullOrEmpty
    }

    It "registers the 'bb' alias resolving to Invoke-Bb" {
        $alias = Get-Alias bb -ErrorAction Stop
        $alias.Definition | Should -Be 'Invoke-Bb'
    }

    It 'exposes Invoke-BbAiQuery as a binary cmdlet' {
        $cmd = Get-Command Invoke-BbAiQuery -ErrorAction Stop
        $cmd.CommandType | Should -Be 'Cmdlet'
    }

    It 'Invoke-BbAiQuery -Stub returns a typed AiResponse without I/O' {
        $response = Invoke-BbAiQuery -UserPrompt 'hello' -Stub
        $response | Should -Not -BeNullOrEmpty
        $response.Command | Should -BeOfType ([string])
        $response.Risk    | Should -BeOfType ([Bb.Core.Models.RiskLevel])
    }
}
