# Product Requirements Document — `bb` CLI

> **Version:** 1.1
> **Status:** Approved for implementation (Milestone 1 may begin)
> **Last updated:** 2026-05-23

---

## 1. Executive Summary

### Problem Statement

Developers and sysadmins working in Windows PowerShell frequently break their workflow to search the web for syntax, parameter names, or one-off command recipes (`find files older than X days`, `kill process by port`, `decode base64 from clipboard`, `compress directory with timestamp`). Each context-switch costs anywhere from 30 seconds to several minutes of lost focus and discards the local session context (CWD, recent errors, available modules) that would make the answer trivial.

### Proposed Solution

`bb` is an AI-powered, context-aware command line for Windows PowerShell. Users type natural-language instructions and receive a single executable PowerShell command that runs in their **current session state** — so `cd`, environment-variable assignments, PSDrives, and module imports all persist for the next prompt. The tool ships as a hybrid PowerShell module (`.psm1` + `PowerShellStandard.Library` C# binary cmdlet) to satisfy both the "stay in-session" requirement and the latency/security requirements that pure script cannot meet.

Tagline: **Stop Googling, start shipping.**

### Success Criteria (KPIs)

| KPI | Target | Measurement |
|---|---|---|
| **Latency (TTFT)** | Time-to-first-token < 600 ms (p50), < 1 s (p90) | Logged locally via `bb --debug`; aggregated only if user opts in |
| **Latency (total)** | End-to-end command-ready < 1.5 s (p50) with streaming | Same |
| **Reliability** | ≥ 95 % of generated commands execute without a PowerShell parse or runtime error | Pester benchmark of 100 curated tasks |
| **Safety** | 100 % of destructive commands classified UNSAFE (zero false negatives on the curated red-team set) | LLM flag ∧ regex blacklist; either trips → UNSAFE |
| **Efficiency** | ≥ 70 % reduction in time-to-command vs. manual web search | A/B timing study with 5+ pilot users on the benchmark task set |
| **Compatibility** | Single module imports cleanly in Windows PowerShell 5.1 *and* PowerShell 7.4+ | CI matrix: `windows-2022` runners on both editions |

---

## 2. User Experience & Functionality

### User Personas

- **Windows Developer.** Lives in PowerShell day-to-day for git, builds, and ad-hoc scripting. Knows the common 20 % of cmdlets, Googles the long tail.
- **Windows Sysadmin.** Manages services, registry, AD, file shares, group policy. Needs syntax for rare administrative cmdlets multiple times per week.
- **Power User on PowerShell 5.1.** Locked to 5.1 by corporate IT, cannot install PowerShell 7. Must be a first-class citizen, not a degraded experience.

### Core User Stories

1. **In-session execution.** *As a developer, I type `bb go to the src folder` and my actual `$PWD` changes — not a child process's CWD.*
2. **Safe-by-default execution.** *As a sysadmin, harmless commands like `Get-ChildItem`, `Get-Process`, `Test-NetConnection` auto-execute so my workflow is not interrupted.*
3. **Hard confirmation for destructive commands.** *As a user, when `bb` proposes `Remove-Item -Recurse -Force`, `Stop-Service`, or `Format-Volume`, I am prompted with the full command, an explanation, a risk level, and must press `E` (or type `yes` for high-risk) to execute.*
4. **Provider portability.** *As a power user, I configure `bb` to use OpenAI, Anthropic (via a gateway), Gemini, Groq, or a local Ollama / LM Studio endpoint — any service that speaks the OpenAI `/v1/chat/completions` schema.*
5. **Graceful offline behavior.** *As a user on a flight, `bb` tells me clearly that it is offline and exits cleanly, rather than hanging or producing a hallucinated command.*

### Acceptance Criteria

- Typing `bb <prompt>` invokes the AI generation pipeline.
- **Session-state persistence:** generated commands like `cd`, `$env:VAR = 'val'`, `$myVar = ...`, and `Import-Module ...` mutate the caller's session, verified by a Pester contract test that asserts `$PWD` after `bb` runs `cd ..`.
- The system classifies every generated command as `low`, `medium`, or `high` risk (see §3) plus a `requires_admin` boolean.
- `low` risk + `requires_admin = false` → auto-execute (configurable via `bb config safety_mode`).
- `medium` risk → present action bar with `[E]xecute [C]opy [R]egenerate [Q]uit`; no auto-execute.
- `high` risk OR `requires_admin = true` → require typed `yes` confirmation, never single-keypress execute.
- The hardcoded regex blacklist in the C# core overrides the LLM's risk classification *upward only* (a command flagged `low` by the LLM but matching `Remove-Item.*-Recurse.*-Force` is forced to `high`).
- Provider configuration and encrypted API keys persist across sessions in `%APPDATA%\bb\`.
- `bb config` and `bb use <provider>` are first-class subcommands.

### Non-Goals (v1)

- macOS or Linux support (PowerShell 7 on non-Windows is out of scope).
- `cmd.exe`, Git Bash, WSL bash, or any shell other than PowerShell Desktop/Core on Windows.
- Multi-step autonomous agent behavior — one prompt produces exactly one command. Pipelines are fine; chained "first do X then do Y" agentic plans are not.
- Cloud telemetry. All timing data stays local unless the user explicitly opts in to share a benchmark report.
- A bundled local LLM. v2.0 will support local LLMs *via* LM Studio / Ollama as drop-in OpenAI-compatible providers, but `bb` itself does not ship a model.

---

## 3. AI System Requirements

### Tool Requirements

- Connect to any standard OpenAI-compatible REST endpoint (POST `/v1/chat/completions`, JSON body, bearer-token auth). Streaming (`text/event-stream`) is the default; non-streaming is the fallback.
- System prompt instructs the model to return a structured JSON object matching the response schema below.
- HTTP/2 preferred (`HttpClientHandler` with `EnableMultipleHttp2Connections`), fall back to HTTP/1.1.

### AI Response Schema (validated in C# before execution)

```jsonc
{
  "command": "Get-ChildItem -Recurse -Filter *.txt",
  "explanation": "Recursively finds all .txt files in the current directory.",
  "risk": "low",            // "low" | "medium" | "high"
  "requires_admin": false   // true if command needs an elevated session
}
```

- Schema is enforced with **strict JSON Schema validation** in the C# core. A schema-fail triggers one re-prompt with the validator error appended; a second schema-fail falls back to fenced-code-block regex extraction with `risk = "high"` and auto-execute disabled.
- The `risk` field is a three-value enum (not a boolean) because empirical evidence on similar tools shows enums produce measurably more consistent outputs than boolean safety flags.
- `requires_admin` is independent of `risk`: `Restart-Service`, for example, is `medium` risk but `requires_admin: true`.

### Evaluation Strategy

| Metric | Method | Target |
|---|---|---|
| **Safety pass rate** | 50 curated destructive prompts → all must be classified `medium` or `high`; never `low` | 100 % (zero false negatives) |
| **Execution pass rate** | 100 curated common prompts → command must parse and execute without error on a clean Windows VM | ≥ 95 % |
| **Latency (TTFT)** | Same 100 prompts × 3 providers × 10 runs | p50 < 600 ms |
| **Schema validity** | Same 100 prompts → JSON must validate on first response | ≥ 98 % |

The red-team prompt set lives in `tests/eval/red_team.jsonl` and the execution set in `tests/eval/exec_set.jsonl`. Both are run in CI on every PR that touches the system prompt or the safety classifier.

---

## 4. Technical Specifications

> Full technical design lives in [`TECHNICAL_DESIGN.md`](./TECHNICAL_DESIGN.md). This section captures only the PRD-level requirements.

### Architecture Overview

**Hybrid module: `.psm1` wrapper + `PowerShellStandard.Library` C# binary cmdlet.**

- The PowerShell module (`bb.psd1` / `bb.psm1`) provides the user-facing `bb` function, the TUI action bar, session-context gathering, and in-session execution via `$ExecutionContext.InvokeCommand.InvokeScript($false, ...)`.
- The C# core (`Bb.Core.dll`, `netstandard2.0`) provides the `Invoke-BbAiQuery` cmdlet, the `CredentialStore` (DPAPI), JSON schema validation, and the hardcoded safety blacklist regex. Co-locating the blacklist in compiled code prevents a malicious profile script from monkey-patching the safety net at runtime.
- Single `.dll` loads natively in both 5.1 (`Desktop`) and 7.x (`Core`) editions via `PowerShellStandard.Library 5.1.1`.

### Integration Points

- **Windows PowerShell 5.1 (`Desktop`)** and **PowerShell 7.4+ (`Core`)** — single manifest with `CompatiblePSEditions = @('Desktop', 'Core')`, `PowerShellVersion = '5.1'`, `DotNetFrameworkVersion = '4.7.2'`.
- **OpenAI-compatible providers** — config-driven, no provider-specific code paths in v1.
- **`PwshSpectreConsole`** is an **optional** dependency loaded at runtime when `$PSVersionTable.PSVersion.Major -ge 7`. It is *not* listed in `RequiredModules` because `PwshSpectreConsole` requires PS 7.2+ and would block import on PS 5.1.

### Connectivity & Failure Matrix (v1)

| Failure mode | Detection | `bb` response |
|---|---|---|
| **No network** | `HttpRequestException` / DNS failure within 2 s | Yellow panel: `"bb is offline. Check your connection or run 'bb --offline' for cached suggestions."` Exit code 2. |
| **API 401 / 403** | HTTP status | Red panel: `"Auth failed for provider <name>. Run 'bb config' to update the key."` Exit code 3. |
| **API 429 (rate limit)** | HTTP 429 + `Retry-After` header | Auto-retry once after the header value; on second failure, suggest `bb use <other-provider>`. Exit code 4. |
| **API 5xx / timeout > 5 s** | HTTP status or `CancellationToken` fired | `"Provider <name> is slow/down. Try 'bb use <other>' or 'bb --retry'."` Exit code 5. |
| **Malformed JSON from model** | `JsonException` or schema validation fails twice | Fall back to fenced-code-block regex extraction; force `risk = "high"`, disable `[E]xecute` key; show raw response under `[V]iew raw`. |
| **User Ctrl+C during streaming** | `Cmdlet.Stopping` fires → `CancellationToken` | Abort HTTP request cleanly, return control to prompt, exit code 130. No half-rendered output. |

### Security & Privacy

- **API keys at rest:** stored encrypted via **DPAPI** (`System.Security.Cryptography.ProtectedData` 8.0.0) in `%APPDATA%\bb\credentials.dat`. Per-user scope, never machine scope. Pin to v8.0.0 explicitly to avoid the runtime/inbox-assembly collision that bites .NET Standard 2.0 modules in PS 7. Use a custom `AssemblyLoadContext` in PS 7 to isolate the v8.0.0 assembly from the inbox 6.x version.
- **Context gathering:** the prompt sent to the LLM includes only the current working directory, `$PSVersionTable.PSVersion`, `$PSVersionTable.OS` (if PS 7), and optionally the most recent error message (`$Error[0].Exception.Message`, truncated to 200 chars). Full environment variables, file contents, and command history are never sent in v1.
- **Safety net is compiled, not scripted:** the regex blacklist lives in the C# core and cannot be monkey-patched by a malicious profile script.
- **No telemetry by default.** `bb --debug` writes timing logs to `%LOCALAPPDATA%\bb\debug.log` locally; nothing is exfiltrated.

---

## 5. Risks & Roadmap

### Phased Rollout

| Phase | Scope |
|---|---|
| **v1.0 MVP** | Hybrid module, core `bb <prompt>`, basic context (CWD, OS version), DPAPI key storage, v1 connectivity failure matrix, manual provider configuration, JSON-schema-validated AI responses, regex blacklist, in-session execution, Pester contract tests, CI for PS 5.1 + 7.4 |
| **v1.1** | Tier-2 **offline mode** (`bb --offline` or auto after 3 consecutive network failures): static curated `commands.jsonl` (~200 entries) searched with BM25 over `prompt → command` pairs. Always labelled "📴 Offline suggestion — verify before running" with auto-execute disabled. |
| **v2.0** | Deep context (recent errors, pipelined output, available modules), local LLM autodetection via LM Studio / Ollama, prompt templates / "skills", multi-turn refinement |

### Key Technical Risks & Mitigations

| Risk | Mitigation |
|---|---|
| **LLM misclassifies a destructive command as `low`** | Hardcoded regex blacklist in the C# core forces `high` on `Remove-*`, `Stop-Service`, `Format-*`, `Set-ExecutionPolicy`, `Invoke-WebRequest \| Invoke-Expression`, etc. The blacklist *upgrades* risk; it never downgrades. |
| **Latency exceeds < 1 s target** | Use fast models by default (gpt-4o-mini, Claude Haiku, Gemini Flash). Measure **TTFT**, not total round-trip, since the UI starts rendering on first token. HTTP/2 connection reuse. |
| **`netstandard.dll` resolution fails on PS 5.1** | Known quirk. Build script ships `netstandard.dll` alongside `Bb.Core.dll` in the module's `bin/` folder; `OnImport` hook validates load on first import. |
| **DPAPI assembly collision in PS 7** | Pin `System.Security.Cryptography.ProtectedData` to **v8.0.0** *and* load `Bb.Core.dll` through a custom `AssemblyLoadContext` on PS 7 to isolate from the inbox version. |
| **`PwshSpectreConsole` blocks 5.1 import** | Do not list in `RequiredModules`. Load conditionally in `bb.psm1` only when `PSVersion.Major -ge 7`; on 5.1 use the in-tree ANSI-escape fallback. |
| **User Ctrl+C crashes the host** | C# cmdlet implements `StopProcessing()` → cancels `CancellationToken` → `HttpClient` request aborts cleanly. `.psm1` TUI loops use `try { ... } finally { [Console]::ResetColor() }` to restore terminal state. |
| **Streaming partial JSON looks malformed mid-flight** | Buffer SSE events until `data: [DONE]`, then validate. Show a spinner while buffering; never render partial commands. |
| **Profile script monkey-patches `Invoke-BbAiQuery`** | C# binary cmdlet, not a script function — cannot be redefined from PowerShell. The safety regex is also compiled-in. |

### Verdict

Planning is complete. Begin **Milestone 1: Scaffolding & CI** per [`TECHNICAL_DESIGN.md`](./TECHNICAL_DESIGN.md#part-2-implementation-plan).
