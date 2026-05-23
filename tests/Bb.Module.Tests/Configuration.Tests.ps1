# -----------------------------------------------------------------------------
# Configuration.Tests.ps1 — covers the PowerShell public surface that touches
# %APPDATA%\bb\config.json + credentials.dat.
#
# We redirect BB_HOME to a per-run temp directory so these tests never touch
# the user's real configuration. All assertions exercise the real DPAPI path
# on Windows; on non-Windows hosts the C# layer would throw before we got
# here, so these tests are Windows-only.
# -----------------------------------------------------------------------------

BeforeDiscovery {
    # $IsWindows is read-only in PS 7+, so we use a private alias on a
    # different name. On PS 5.1 there is no $IsWindows automatic variable,
    # so we default to $true (PS 5.1 is Windows-only).
    $script:BbOnWindows = if ($PSVersionTable.PSEdition -eq 'Core') {
        [bool]$IsWindows
    } else {
        $true
    }
}

BeforeAll {
    $script:RepoRoot     = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $script:ManifestPath = Join-Path $script:RepoRoot 'module/bb.psd1'
    $script:TempBbHome   = Join-Path ([System.IO.Path]::GetTempPath()) ("bb-pester-" + [guid]::NewGuid().ToString('N'))
    $script:OriginalBbHome = $env:BB_HOME
    $env:BB_HOME = $script:TempBbHome
    New-Item -ItemType Directory -Path $script:TempBbHome -Force | Out-Null

    Import-Module $script:ManifestPath -Force -ErrorAction Stop
}

AfterAll {
    Remove-Module bb -Force -ErrorAction Ignore
    $env:BB_HOME = $script:OriginalBbHome
    if ($script:TempBbHome -and (Test-Path -LiteralPath $script:TempBbHome)) {
        Remove-Item -LiteralPath $script:TempBbHome -Recurse -Force -ErrorAction Ignore
    }
}

Describe 'Set-BbConfig' -Skip:(-not $script:BbOnWindows) {
    BeforeEach {
        # Each test starts from a clean slate so order does not matter.
        Get-ChildItem -LiteralPath $script:TempBbHome -ErrorAction Ignore | Remove-Item -Force -Recurse -ErrorAction Ignore
    }

    It 'creates a new provider with defaults when nothing else is set' {
        $result = Set-BbConfig -Provider openai -ApiKey 'sk-test' -Confirm:$false
        $result.Provider       | Should -Be 'openai'
        $result.Endpoint       | Should -Be 'https://api.openai.com/v1/chat/completions'
        $result.Model          | Should -Be 'gpt-4o-mini'
        $result.Stream         | Should -BeTrue
        $result.TimeoutSeconds | Should -Be 8
        $result.Active         | Should -BeTrue
        $result.HasApiKey      | Should -BeTrue
    }

    It 'persists the encrypted key (file never contains plaintext)' {
        Set-BbConfig -Provider openai -ApiKey 'sk-very-unique-marker-xyz' -Confirm:$false | Out-Null
        $credsPath = Join-Path $script:TempBbHome 'credentials.dat'
        (Get-Content -Raw -LiteralPath $credsPath) | Should -Not -Match 'sk-very-unique-marker-xyz'
    }

    It 'writes a config.json with the provider entry' {
        Set-BbConfig -Provider openai -ApiKey 'sk' -Endpoint 'https://example.test/v1/c' -Model 'gpt-4' -Confirm:$false | Out-Null
        $configPath = Join-Path $script:TempBbHome 'config.json'
        $raw = Get-Content -Raw -LiteralPath $configPath
        $raw | Should -Match '"openai"'
        $raw | Should -Match 'https://example.test/v1/c'
        $raw | Should -Match 'gpt-4'
    }

    It 'updates an existing provider without an ApiKey, keeping the stored key' {
        Set-BbConfig -Provider openai -ApiKey 'sk-original' -Confirm:$false | Out-Null
        Set-BbConfig -Provider openai -Model 'gpt-4o' -Confirm:$false | Out-Null
        $cfg = Get-BbConfig -Provider openai
        $cfg.Model     | Should -Be 'gpt-4o'
        $cfg.HasApiKey | Should -BeTrue
    }

    It 'leaves the first-configured provider active when adding a second' {
        Set-BbConfig -Provider openai -ApiKey 'k1' -Confirm:$false | Out-Null
        Set-BbConfig -Provider ollama -ApiKey 'k2' -Endpoint 'http://localhost:11434/v1/chat/completions' -Model 'llama3.2' -Confirm:$false | Out-Null

        (Get-BbConfig -Provider openai).Active | Should -BeTrue
        (Get-BbConfig -Provider ollama).Active | Should -BeFalse
    }

    It 'flips active when -SetActive is passed' {
        Set-BbConfig -Provider openai -ApiKey 'k1' -Confirm:$false | Out-Null
        Set-BbConfig -Provider ollama -ApiKey 'k2' -Endpoint 'http://localhost:11434/v1/chat/completions' -Model 'llama3.2' -SetActive -Confirm:$false | Out-Null

        (Get-BbConfig -Provider ollama).Active | Should -BeTrue
        (Get-BbConfig -Provider openai).Active | Should -BeFalse
    }

    It 'rejects empty -ApiKey' {
        { Set-BbConfig -Provider openai -ApiKey '' -Confirm:$false } | Should -Throw -ExpectedMessage '*passed empty*'
    }

    It 'respects -WhatIf without writing files' {
        Set-BbConfig -Provider openai -ApiKey 'sk' -WhatIf | Out-Null
        (Test-Path (Join-Path $script:TempBbHome 'config.json')) | Should -BeFalse
    }
}

Describe 'Use-BbProvider' -Skip:(-not $script:BbOnWindows) {
    BeforeEach {
        Get-ChildItem -LiteralPath $script:TempBbHome -ErrorAction Ignore | Remove-Item -Force -Recurse -ErrorAction Ignore
        Set-BbConfig -Provider openai -ApiKey 'k1' -Confirm:$false | Out-Null
        Set-BbConfig -Provider ollama -ApiKey 'k2' -Endpoint 'http://localhost:11434/v1/chat/completions' -Model 'llama3.2' -Confirm:$false | Out-Null
    }

    It 'switches active provider' {
        Use-BbProvider ollama -Confirm:$false | Out-Null
        (Get-BbConfig -Provider ollama).Active | Should -BeTrue
        (Get-BbConfig -Provider openai).Active | Should -BeFalse
    }

    It 'switches back by case-insensitive name' {
        Use-BbProvider 'OPENAI' -Confirm:$false | Out-Null
        (Get-BbConfig -Provider openai).Active | Should -BeTrue
    }

    It 'throws when the provider is unknown' {
        { Use-BbProvider gemini -Confirm:$false } | Should -Throw -ExpectedMessage '*not configured*'
    }
}

Describe 'Get-BbConfig' -Skip:(-not $script:BbOnWindows) {
    BeforeEach {
        Get-ChildItem -LiteralPath $script:TempBbHome -ErrorAction Ignore | Remove-Item -Force -Recurse -ErrorAction Ignore
    }

    It 'returns nothing when no providers are configured' {
        $rows = @(Get-BbConfig)
        $rows.Count | Should -Be 0
    }

    It 'enumerates all configured providers' {
        Set-BbConfig -Provider a -ApiKey 'k' -Confirm:$false | Out-Null
        Set-BbConfig -Provider b -ApiKey 'k' -Endpoint 'http://x/v1/c' -Model 'm' -Confirm:$false | Out-Null

        $rows = @(Get-BbConfig | Sort-Object Provider)
        $rows.Count            | Should -Be 2
        $rows[0].Provider      | Should -Be 'a'
        $rows[1].Provider      | Should -Be 'b'
    }

    It 'never includes the API key value itself' {
        Set-BbConfig -Provider openai -ApiKey 'sk-leaky-key-marker' -Confirm:$false | Out-Null
        $rendered = Get-BbConfig | Out-String
        $rendered | Should -Not -Match 'sk-leaky-key-marker'
    }
}

Describe 'Remove-BbProvider' -Skip:(-not $script:BbOnWindows) {
    BeforeEach {
        Get-ChildItem -LiteralPath $script:TempBbHome -ErrorAction Ignore | Remove-Item -Force -Recurse -ErrorAction Ignore
        Set-BbConfig -Provider openai -ApiKey 'k' -Confirm:$false | Out-Null
    }

    It 'removes the provider and its stored secret' {
        Remove-BbProvider -Name openai -Confirm:$false
        @(Get-BbConfig).Count | Should -Be 0
    }

    It 'clears the active pointer when the active provider is removed' {
        Remove-BbProvider -Name openai -Confirm:$false
        Set-BbConfig -Provider ollama -ApiKey 'k' -Endpoint 'http://localhost:11434/v1/chat/completions' -Model 'llama3.2' -Confirm:$false | Out-Null
        # After removing the previously-active provider, the new provider should become active.
        (Get-BbConfig -Provider ollama).Active | Should -BeTrue
    }

    It 'warns but does not throw when removing an unknown provider' {
        $warnings = @()
        Remove-BbProvider -Name does-not-exist -Confirm:$false -WarningVariable warnings -WarningAction SilentlyContinue
        $warnings.Count | Should -BeGreaterThan 0
    }
}
