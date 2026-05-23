# Technical Design — `bb` CLI

> **Companion document to** [`PRD.md`](./PRD.md). This document specifies the internal architecture, file layout, data schemas, and implementation milestones. Updates to this document do **not** require a PRD revision unless they change a PRD-level acceptance criterion or KPI.

---

## Part 1 — Technical Design

### 1. Hybrid Architecture & Folder Structure

```
bb/                                  # repository root
├── README.md
├── LICENSE
├── .gitignore
├── docs/
│   ├── PRD.md
│   └── TECHNICAL_DESIGN.md          # this file
├── module/                          # the shipped PowerShell module
│   ├── bb.psd1                      # manifest, CompatiblePSEditions = @('Desktop','Core')
│   ├── bb.psm1                      # script wrapper: TUI, context, execution
│   ├── bin/
│   │   ├── Bb.Core.dll              # compiled netstandard2.0 binary
│   │   └── netstandard.dll          # shipped to work around the 5.1 resolution quirk
│   ├── private/
│   │   ├── FallbackAnsi.ps1         # ANSI-escape renderer for PS 5.1
│   │   ├── SpectreRenderer.ps1      # Spectre.Console renderer for PS 7+
│   │   └── ContextGatherer.ps1
│   └── public/
│       ├── Invoke-Bb.ps1            # the user-facing 'bb' function (alias: bb)
│       ├── Set-BbConfig.ps1
│       └── Use-BbProvider.ps1
├── src/
│   └── Bb.Core/
│       ├── Bb.Core.csproj           # netstandard2.0
│       ├── Commands/
│       │   └── InvokeBbAiQueryCommand.cs
│       ├── Services/
│       │   ├── OpenAiCompatibleClient.cs
│       │   ├── StreamingJsonAssembler.cs
│       │   ├── ResponseSchemaValidator.cs
│       │   ├── CredentialStore.cs               # DPAPI
│       │   └── SafetyClassifier.cs              # compiled-in regex blacklist
│       └── Models/
│           ├── AiResponse.cs
│           ├── ProviderConfig.cs
│           └── RiskLevel.cs
├── tests/
│   ├── Bb.Core.Tests/               # xUnit tests for the C# core
│   │   └── Bb.Core.Tests.csproj
│   ├── Bb.Module.Tests/             # Pester 5 tests for the .psm1
│   │   ├── Bb.Module.Tests.ps1
│   │   └── SessionPersistence.Tests.ps1    # the contract test
│   └── eval/
│       ├── red_team.jsonl
│       └── exec_set.jsonl
├── scripts/
│   ├── build.ps1                    # dotnet build + module assembly
│   ├── test.ps1                     # Pester + xUnit
│   └── publish.ps1                  # Publish-Module to PSGallery (CI-only)
├── config/
│   └── providers.default.json       # built-in provider list (no keys)
├── .github/
│   └── workflows/
│       ├── ci.yml                   # build + analyze + test on PS 5.1 and 7.4
│       └── release.yml              # publish on tag push
└── Bb.sln                           # Visual Studio solution
```

### 2. The C# Core (`Bb.Core.dll`)

**Target:** `netstandard2.0`. **Library:** `PowerShellStandard.Library 5.1.1` (pinned). **Output:** single `Bb.Core.dll` that loads in both PS 5.1 and PS 7.x without recompilation.

#### 2.1 `Invoke-BbAiQuery` cmdlet

The latency-sensitive entry point. Called from `bb.psm1` once the user prompt and context are assembled.

- Accepts a fully-rendered system + user prompt, a `ProviderConfig`, and an optional `CancellationToken` parameter.
- Owns a singleton `HttpClient` with HTTP/2 enabled (`SocketsHttpHandler` on PS 7, `WinHttpHandler` on 5.1; both can be configured via `HttpRequestMessage.Version`).
- Streams server-sent events from `text/event-stream` responses, assembling a single accumulating JSON object via `StreamingJsonAssembler`.
- On stream completion, hands the buffer to `ResponseSchemaValidator`. Validation failure triggers exactly one automatic re-prompt with the validator error appended; a second failure raises a non-terminating error tagged `BbSchemaInvalid` so the `.psm1` can fall back to fenced-code-block extraction.
- `StopProcessing()` cancels the token, which aborts the in-flight `HttpClient.SendAsync` call cleanly and surfaces as a `PipelineStoppedException` to the wrapper.

#### 2.2 `CredentialStore` (DPAPI)

- Wraps `System.Security.Cryptography.ProtectedData` (NuGet package version **8.0.0**, pinned).
- Storage path: `%APPDATA%\bb\credentials.dat`. Scope: `DataProtectionScope.CurrentUser`. Never `LocalMachine`.
- Public surface: `Get-BbSecret -Name <provider>` and `Set-BbSecret -Name <provider> -Value <plaintext>` (both implemented as binary cmdlets so the plaintext never sits in `.psm1` variables).
- Throws a typed `BbPlatformNotSupportedException` on non-Windows runtimes (defensive — PRD declares Windows-only, but a clear error beats a `PlatformNotSupportedException` from the BCL).

#### 2.3 `SafetyClassifier`

- Hardcoded regex array, compiled into `Bb.Core.dll` so it cannot be patched at runtime.
- Operates as an **upgrade-only** filter: takes the LLM's risk classification and returns `max(llm_risk, regex_risk)`.
- Initial patterns (non-exhaustive, finalized in Milestone 4):
  - `Remove-Item.*-Recurse` → `high`
  - `Remove-Item.*-Force` (without explicit path scope) → `high`
  - `Format-Volume|Clear-Disk|Remove-Partition` → `high`
  - `Stop-Service|Restart-Service|Stop-Process -Force` → `medium`, `requires_admin=true`
  - `Set-ExecutionPolicy` → `high`, `requires_admin=true`
  - `Invoke-WebRequest.*\|.*Invoke-Expression` → `high`
  - `irm.*\|.*iex` → `high`
  - `Set-ItemProperty.*HKLM:` → `high`, `requires_admin=true`
- Patterns are *additive* and intentionally err on the side of false positives. Users can override per-command with `bb config safety_mode strict|normal|permissive`, but `strict` is the default and the only mode that goes through GA.

#### 2.4 `ResponseSchemaValidator`

JSON Schema 2020-12 validator over the schema declared in [`PRD.md` §3](./PRD.md#ai-response-schema-validated-in-c-before-execution). Implementation uses `JsonSchema.Net` (pinned). The schema lives at `src/Bb.Core/Models/ai-response.schema.json` and is embedded as an assembly resource so it cannot drift from the compiled validator.

### 3. The PowerShell Wrapper (`bb.psm1`)

**Responsibility:** user experience, session integration, safety enforcement at the UI layer.

#### 3.1 The `bb` function

```powershell
function Invoke-Bb {
    [CmdletBinding()]
    param([Parameter(ValueFromRemainingArguments)] [string[]] $Prompt)
    # 1. Gather context: $PWD, $PSVersionTable, optionally $Error[0]
    # 2. Resolve provider + API key (via Get-BbSecret cmdlet from the binary core)
    # 3. Invoke-BbAiQuery -SystemPrompt $sys -UserPrompt $prompt -Config $cfg
    # 4. Render TUI action bar, await keypress
    # 5. On [E]: $ExecutionContext.InvokeCommand.InvokeScript($false, $response.Command, $null, $null)
}
Set-Alias bb Invoke-Bb
```

The critical line is the `InvokeScript($false, ...)` call — passing `$false` for `useNewScope` ensures the generated script runs in the caller's session state, which is the entire reason this is a `.psm1` and not a standalone binary.

#### 3.2 Action bar TUI

- **PS 7.4+ path:** if `PwshSpectreConsole` is installed, use it for the action bar (rounded panel, colored risk badge, syntax-highlighted command). If not installed, prompt once on first run to suggest `Install-Module PwshSpectreConsole`.
- **PS 5.1 path:** `private/FallbackAnsi.ps1` renders the same action bar using raw ANSI escape sequences via `Write-Host`. Detect VT support via `[Console]::IsOutputRedirected` and `$Host.UI.SupportsVirtualTerminal` (5.1.15063+) and fall back to plain text if unavailable.
- Keys (universal):
  - `E` — Execute (gated by risk level; high-risk requires typed `yes`)
  - `C` — Copy command to clipboard via `Set-Clipboard`
  - `R` — Regenerate (re-prompt with `temperature += 0.2`)
  - `V` — View raw provider response (debugging aid)
  - `Q` / `Esc` — Quit without executing
- The TUI loop is wrapped in `try { ... } finally { [Console]::ResetColor(); [Console]::CursorVisible = $true }` so a Ctrl+C never leaves the terminal in a broken state.

#### 3.3 In-session execution contract

A **Pester contract test** (`tests/Bb.Module.Tests/SessionPersistence.Tests.ps1`) lives in CI and asserts:

```powershell
It 'changes $PWD in the caller scope when bb executes cd' {
    $before = $PWD.Path
    Mock Invoke-BbAiQuery { @{ Command = 'Set-Location ..'; Risk = 'low'; RequiresAdmin = $false } }
    Mock Read-Host { 'y' }
    Invoke-Bb 'go up one directory'
    $PWD.Path | Should -Not -Be $before
    $PWD.Path | Should -Be (Split-Path $before -Parent)
}
```

If this test ever fails, **the entire build fails** — it is the implementation-path decision codified as a regression guard.

#### 3.4 Assembly loading on PS 7

On PS 7, `bb.psm1` loads `Bb.Core.dll` through a custom `AssemblyLoadContext` to isolate the pinned `System.Security.Cryptography.ProtectedData 8.0.0` assembly from PS 7's inbox 6.x version. The ALC code lives in `private/AlcLoader.ps1` and is only invoked when `$PSVersionTable.PSVersion.Major -ge 7`. On PS 5.1 the DLL is loaded normally via `Add-Type -Path` because the inbox-collision problem does not exist there.

### 4. Data Schemas

#### 4.1 User configuration — `%APPDATA%\bb\config.json`

```jsonc
{
  "version": 1,
  "current_provider": "openai",
  "providers": {
    "openai": {
      "endpoint": "https://api.openai.com/v1/chat/completions",
      "model": "gpt-4o-mini",
      "stream": true,
      "timeout_seconds": 8
    },
    "ollama-local": {
      "endpoint": "http://localhost:11434/v1/chat/completions",
      "model": "qwen2.5-coder:7b",
      "stream": true,
      "timeout_seconds": 30
    }
  },
  "safety_mode": "strict",
  "telemetry": {
    "local_debug_log": false
  }
}
```

Default file is created on first `bb config` run. Schema version is included for forward compatibility. Keys are *never* stored in `config.json` — they live encrypted in `credentials.dat` next to it.

#### 4.2 AI response schema (`src/Bb.Core/Models/ai-response.schema.json`)

```jsonc
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "required": ["command", "risk", "requires_admin"],
  "additionalProperties": false,
  "properties": {
    "command":         { "type": "string", "minLength": 1, "maxLength": 4096 },
    "explanation":     { "type": "string", "maxLength": 1024 },
    "risk":            { "type": "string", "enum": ["low", "medium", "high"] },
    "requires_admin":  { "type": "boolean" }
  }
}
```

#### 4.3 System prompt (excerpted)

> *You are `bb`, a PowerShell command synthesizer. Given a user's natural-language request and their session context, you respond with EXACTLY ONE JSON object matching this schema: `{command, explanation, risk, requires_admin}`. The `command` MUST be a single valid PowerShell statement or pipeline runnable in `$PSVersionTable.PSVersion` >= 5.1. Classify `risk` as `high` if the command deletes data, mutates system configuration, stops services, or executes downloaded code; `medium` if it modifies the user's files or environment in non-trivial ways; `low` if it only reads state. Set `requires_admin` true when the command needs an elevated session.*

Full system prompt lives at `src/Bb.Core/Resources/system-prompt.md` and is embedded as a resource.

---

## Part 2 — Implementation Plan

### Milestone 1 — Scaffolding & CI (Week 1)

- [ ] Initialize repo structure per §1 (this PR landed the documentation foundation; PR 2 lands the source tree).
- [ ] `Bb.Core.csproj` targeting `netstandard2.0`, referencing `PowerShellStandard.Library 5.1.1`.
- [ ] `Bb.sln` with `Bb.Core` and `Bb.Core.Tests`.
- [ ] Draft `bb.psd1` with: `PowerShellVersion = '5.1'`, `DotNetFrameworkVersion = '4.7.2'`, `CompatiblePSEditions = @('Desktop','Core')`, `NestedModules = @('bin/Bb.Core.dll')`, explicit `CmdletsToExport`, `FunctionsToExport`, `AliasesToExport` arrays (no wildcards — wildcards fail PSGallery analysis and slow auto-loading).
- [ ] Stub `Invoke-Bb` that returns the input prompt unchanged (smoke test for the wrapper plumbing).
- [ ] `.github/workflows/ci.yml` on `windows-latest`, running in order: `dotnet build -c Release src/Bb.Core` → `Invoke-ScriptAnalyzer -Path . -Recurse -Settings PSGallery -EnableExit` → `Invoke-Pester -CI -Output Detailed` → on tag push, `Publish-Module -NuGetApiKey $env:PSGALLERY_KEY`.
- [ ] Matrix the Pester run across PS 5.1 (`actions/setup-powershell@v1` with `pwsh: false`) and PS 7.4 (`pwsh: true`).
- [ ] Configure `PwshSpectreConsole` as opt-in (NOT in `RequiredModules`).

**Exit criterion:** `Import-Module ./module/bb.psd1` succeeds on both editions in CI; `bb hello` echoes `hello`; PSScriptAnalyzer is clean.

### Milestone 2 — C# Core & Security (Week 2)

- [ ] `CredentialStore` with DPAPI; pin `System.Security.Cryptography.ProtectedData` to `8.0.0`.
- [ ] Custom `AssemblyLoadContext` loader for PS 7 (`private/AlcLoader.ps1`); plain `Add-Type` on PS 5.1.
- [ ] `OpenAiCompatibleClient`: `HttpClient` singleton, HTTP/2 negotiation, SSE streaming, `CancellationToken` plumbed through.
- [ ] `StreamingJsonAssembler` that buffers `data:` events until `[DONE]`.
- [ ] `ResponseSchemaValidator` with `JsonSchema.Net`.
- [ ] `Invoke-BbAiQuery` cmdlet implementing `StopProcessing()` → cancels token.
- [ ] xUnit tests against a mocked `HttpClientHandler` covering: 200 streaming, 401, 429 with `Retry-After`, 5xx, malformed JSON, mid-stream cancellation.
- [ ] Ship `netstandard.dll` in `module/bin/` and validate load via `OnImport` hook.

**Exit criterion:** xUnit suite green; integration test `Invoke-BbAiQuery -SystemPrompt ... -UserPrompt ...` returns a validated `AiResponse` against a stubbed local SSE server on both editions.

### Milestone 3 — PowerShell Wrapper & TUI (Week 3)

- [ ] `Invoke-Bb` function with context gathering (CWD, `$PSVersionTable`, optional last error truncated).
- [ ] Action-bar TUI: Spectre on PS 7+, ANSI fallback on 5.1.
- [ ] Keypress loop with `try/finally` guarding terminal state.
- [ ] In-session execution via `$ExecutionContext.InvokeCommand.InvokeScript($false, ...)`.
- [ ] Pester contract test `SessionPersistence.Tests.ps1` proving `cd` mutates `$PWD`.

**Exit criterion:** Manually running `bb find all txt files in current directory` on both PS 5.1 and PS 7.4 produces the action bar, executes on `E`, and quits cleanly on `Q`/`Ctrl+C`. Contract test passes in CI.

### Milestone 4 — Safety & Intelligence (Week 4)

- [ ] Finalize the safety regex set in `SafetyClassifier`; freeze for v1.
- [ ] Wire the upgrade-only safety chain: `bb.psm1` calls `SafetyClassifier.Apply(llmResponse)` before rendering the TUI.
- [ ] `tests/eval/red_team.jsonl` with 50 destructive prompts; runs in CI; *any* false negative fails the build.
- [ ] `tests/eval/exec_set.jsonl` with 100 common prompts; runs in CI nightly against a configured provider (key in CI secrets); reports execution success rate.
- [ ] Tighten the system prompt and embed it as a resource.
- [ ] **Cap red-team work at 2 days**; reserve remaining capacity in this milestone for documentation drafting in parallel with M5.

**Exit criterion:** Red-team set has zero false negatives in CI. Nightly exec set ≥ 95 %.

### Milestone 4.5 — Local debug instrumentation (½ week, parallelizable with M5)

- [ ] `bb --debug` flag enables local-only timing logs (TTFT, total, status code, retry count) to `%LOCALAPPDATA%\bb\debug.log`.
- [ ] Log rotation: keep the 10 most recent runs.
- [ ] **No network egress.** This is the empirical instrument the PRD's KPI section depends on; it must exist before GA so we can validate the < 1 s TTFT claim.

**Exit criterion:** `bb --debug hello` writes a parseable JSONL entry; opening the log shows TTFT and total.

### Milestone 5 — Polish & Release (Week 5)

- [ ] Implement the full v1 connectivity failure matrix with colored panels and distinct exit codes.
- [ ] `bb config` (interactive provider setup) and `bb use <provider>` helper commands.
- [ ] `bb --version`, `bb --help`, `bb --offline` (stub for v1.1).
- [ ] README polish, animated demo (asciinema or terminalizer), `comparable to ...` positioning.
- [ ] `Publish-Module bb -Repository PSGallery` on `v1.0.0` tag push. Module ID fallback to `BbCli` if `bb` is taken on the gallery (the `bb` *command alias* stays unchanged).
- [ ] Tag `v1.0.0`.

**Exit criterion:** A clean Windows 11 + Windows Server 2022 VM can `Install-Module bb` (or `BbCli`), set an API key, and run `bb hello world` end-to-end on both PS editions.

---

## Open Questions (track in issues, not blockers)

1. **Spectre.Console direct vs. via `PwshSpectreConsole`.** If `PwshSpectreConsole` proves friction-heavy in 7.x, consider statically linking `Spectre.Console` against the C# core and exposing only what we need. Decision deferred to end of M3 after measuring import latency.
2. **Anthropic native API support.** The OpenAI-compatible path covers Anthropic via gateways like LiteLLM, but a native `/v1/messages` adapter may be worth ½ day of work in v1.1.
3. **Module name on PSGallery.** Two-letter names are likely taken. Confirm availability of `bb`; fall back to `BbCli` or `posh-bb` for the gallery ID while keeping the `bb` *command alias* unchanged.
