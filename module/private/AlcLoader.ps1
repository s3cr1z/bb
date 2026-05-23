function Initialize-BbAssemblyLoadContext {
    <#
    .SYNOPSIS
        Load Bb.Core.dll through a custom AssemblyLoadContext on PS 7.

    .DESCRIPTION
        PR 2 stub. PR 3 implements the actual ALC isolation to keep our
        pinned System.Security.Cryptography.ProtectedData 8.0.0 (DPAPI)
        from colliding with PowerShell 7's inbox 6.x version. On PS 5.1
        this function is not called - NestedModules in bb.psd1 handles the
        load normally because the inbox-collision problem does not exist.

    .PARAMETER ModuleRoot
        Absolute path to the directory containing bin/Bb.Core.dll.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $ModuleRoot
    )

    # PR 2 no-op. The contract is intentional: PR 3 can flesh this out
    # without changing any call site.
    Write-Verbose ("Initialize-BbAssemblyLoadContext: stub for ModuleRoot='{0}' (real ALC isolation lands in PR 3)" -f $ModuleRoot)
}
