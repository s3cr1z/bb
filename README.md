# bb

> Stop Googling, start shipping.

`bb` is an AI-powered, context-aware command line for **Windows PowerShell**. Type natural-language instructions, get safe PowerShell commands in seconds, and execute them in your current session.

```powershell
PS C:\Users\you\src\my-app> bb find all txt files modified today
# → Get-ChildItem -Recurse -Filter *.txt | Where-Object { $_.LastWriteTime -ge (Get-Date).Date }
# [E]xecute  [C]opy  [R]egenerate  [Q]uit
```

## Status

**Pre-MVP — planning phase.** The Product Requirements Document and Technical Design are checked in. Implementation work begins in subsequent PRs. See [`docs/PRD.md`](docs/PRD.md) and [`docs/TECHNICAL_DESIGN.md`](docs/TECHNICAL_DESIGN.md) for the spec.

## Goals

- **Native PowerShell.** Generated commands run in the caller's session state, so `cd ..\src`, `$env:FOO = 'bar'`, and `$myVar = ...` actually persist.
- **PowerShell 5.1 floor, 7.4+ preferred.** Single module supporting both Desktop and Core editions via `PowerShellStandard.Library`.
- **Provider-agnostic.** Works with any OpenAI-compatible REST endpoint — OpenAI, Anthropic (via gateway), Gemini, Groq, Ollama, LM Studio.
- **Safe by default.** Read-only commands auto-execute; mutating commands prompt for confirmation. A hardcoded blacklist in the C# core backstops the LLM's safety classification.
- **Fast.** HTTP/2 streaming, Time-to-First-Token measured (not just total).

## Non-goals (v1)

- macOS / Linux support.
- `cmd.exe`, Git Bash, WSL, or any non-PowerShell shell.
- Autonomous multi-step agent behavior — one prompt produces one command.

## Architecture (one-paragraph)

Hybrid module: a `.psm1` script module wrapping a `PowerShellStandard.Library` C# binary cmdlet (`Bb.Core.dll`, `netstandard2.0`). The C# core owns latency-sensitive and security-sensitive work — `HttpClient` with HTTP/2 streaming, JSON schema validation, DPAPI-encrypted credential storage, and the hardcoded safety blacklist regex. The `.psm1` owns the TUI, session-context gathering, and in-session execution via `$ExecutionContext.InvokeCommand.InvokeScript($false, ...)`. Full design in [`docs/TECHNICAL_DESIGN.md`](docs/TECHNICAL_DESIGN.md).

## Roadmap

| Phase | Status | Scope |
|---|---|---|
| **v1.0 MVP** | planned | Core `bb <prompt>`, basic context (CWD), DPAPI key storage, v1 connectivity failure matrix |
| **v1.1** | planned | Tier-2 offline mode (`bb --offline`) with curated `commands.jsonl` + BM25 search |
| **v2.0** | planned | Deep context (recent errors, pipeline output), local LLM support (LM Studio / Ollama) |

## License

MIT — see [`LICENSE`](LICENSE).
