# ---------------------------------------------------------------------------
# SpectreRenderer.ps1 - placeholder for the PS 7+ rich TUI renderer.
#
# PR 2 stub: this file intentionally defines nothing. The action bar is
# rendered by Write-BbActionBar in private/FallbackAnsi.ps1 on all editions
# until PR 3 Milestone 3 wires up PwshSpectreConsole behind a conditional
# load.
#
# Design note: PwshSpectreConsole MUST NOT be added to RequiredModules in
# bb.psd1 because it requires PowerShell 7.2+, which would hard-fail
# Import-Module on PS 5.1 - the very audience we target.
# ---------------------------------------------------------------------------

# Intentionally empty in PR 2.
