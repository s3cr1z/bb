@{
    # --- Identity ------------------------------------------------------------
    RootModule           = 'bb.psm1'
    ModuleVersion        = '0.1.0'
    GUID                 = '928fa031-0d93-40b5-8fa8-e473a8629784'
    Author               = 's3cr1z'
    CompanyName          = 's3cr1z'
    Copyright            = '(c) 2026 s3cr1z. Released under the MIT License.'
    Description          = 'AI-powered, context-aware command line for Windows PowerShell. Type natural-language instructions, get safe commands in seconds. Single hybrid module compatible with PowerShell 5.1 and 7.4+.'

    # --- Cross-edition compatibility -----------------------------------------
    # We support both Windows PowerShell 5.1 (Desktop) and PowerShell 7.4+ (Core)
    # via a single PowerShellStandard.Library 5.1.1 binary. Listing both editions
    # here is enforced by the Pester contract test in tests/Bb.Module.Tests/.
    PowerShellVersion       = '5.1'
    CompatiblePSEditions    = @('Desktop', 'Core')
    DotNetFrameworkVersion  = '4.7.2'

    # --- Binary cmdlet wiring ------------------------------------------------
    # NestedModules auto-imports Bb.Core.dll on Import-Module so the binary
    # cmdlets (Invoke-BbAiQuery in PR 2, plus Get-BbSecret / Set-BbSecret in
    # PR 3) are available without an extra Add-Type call. The .dll is staged
    # into module/bin/ by the post-build target in src/Bb.Core/Bb.Core.csproj.
    NestedModules        = @('bin\Bb.Core.dll')

    # --- Explicit exports ----------------------------------------------------
    # No wildcards — wildcards block PSGallery's analyzer at publish time and
    # slow down module auto-loading. We list every public surface explicitly.
    FunctionsToExport    = @('Invoke-Bb', 'Set-BbConfig', 'Use-BbProvider', 'Get-BbConfig', 'Remove-BbProvider')
    CmdletsToExport      = @('Invoke-BbAiQuery')
    AliasesToExport      = @('bb')
    VariablesToExport    = @()

    # --- Optional dependency, NOT required -----------------------------------
    # PwshSpectreConsole requires PS 7.2+, so listing it here would hard-fail
    # Import-Module on PS 5.1 — the very audience we are targeting. It is
    # loaded conditionally at runtime in private/SpectreRenderer.ps1.
    # RequiredModules    = @()  -- intentionally empty

    # --- PSData --------------------------------------------------------------
    PrivateData = @{
        PSData = @{
            Tags         = @('PowerShell', 'AI', 'CLI', 'OpenAI', 'productivity', 'Windows', 'PSEdition_Desktop', 'PSEdition_Core')
            LicenseUri   = 'https://github.com/s3cr1z/bb/blob/main/LICENSE'
            ProjectUri   = 'https://github.com/s3cr1z/bb'
            IconUri      = ''
            ReleaseNotes = 'MVP (PR 3): real provider config, DPAPI-backed credential storage, OpenAI-compatible streaming chat completions, JSON-schema validation, upgrade-only regex safety classifier, interactive action bar with [Y/n/edit] confirmation, and bb --debug timing instrumentation.'
        }
    }
}
