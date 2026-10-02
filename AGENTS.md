# AGENTS.md — Standing instructions for the coding agent

Project: **LocalFileAgent**, an offline, read-only, **Arabic-first** Windows desktop agent (C# / .NET 10 / WPF) that searches local files (PDF, DOCX, images, scans, text) by name, text, OCR text, image content, and meaning, using local models through Ollama.

Read `docs/AGENT_BUILD_PLAN.md` before doing anything. It is the source of truth for scope, order of work, Arabic requirements, risks, and gates. If this file and the plan disagree, stop and ask.

## Hard rules (never break these)

1. **Never modify, move, rename, or delete files outside the repository and `%LOCALAPPDATA%\LocalFileAgent\`.** The product itself is read-only on user files; the development workflow must respect that too.
2. **No network use by the product.** Model endpoints must be loopback (`127.0.0.1` / `::1`) only. The only allowed internet use is the one-time model/package download in M0/M3, and only after the user approves the list and sizes.
3. **Private data stays private.** Files in `corpus/private/` are the user's real Arabic documents. Do not copy them elsewhere, print their contents into logs/reports/commits, or send them to any service. Benchmarks on private data report metrics only, never text.
4. **Do not index real drives or folders during development.** Use `corpus/synthetic/` and `corpus/private/` only. Indexing anything else needs explicit user approval.
5. **No destructive or system-wide commands without asking** (registry edits, `Add-WindowsCapability`, driver/firewall changes, uninstalling anything, deleting Ollama models, changing global Ollama environment variables). Admin actions are the user's to run; give them the exact command.
6. **Do not invent APIs or package versions.** Compile and run everything. Check NuGet package names/versions and licenses with `dotnet package search` / the package page before adding. Avoid commercial/licensed libraries (e.g., IronOCR) unless the user approves.
7. **Never claim a task is done without evidence** (passing tests, benchmark output, screenshot, or log excerpt saved under `docs/`).

## Arabic rules

- Arabic is the primary language. Nothing may assume left-to-right text, ASCII-only names, Western digits, or UTF-8-only encodings.
- All text going into search or embeddings passes through `ITextNormalizer`. **Store the original text too**; normalized text is a derived copy.
- Do not rely on SQLite's `unicode61` tokenizer to handle Arabic marks (tashkeel/tatweel/digits). Normalize in C# first.
- Tests with tricky Arabic must use `\uXXXX` escapes or a verified UTF-8 file so editors cannot corrupt them. Include: with/without diacritics, tatweel, alef/ya/ta-marbuta variants, Arabic-Indic digits, mixed Arabic/English, presentation-form glyphs, reversed text.
- UI strings live in resource files (`ar-EG` and `en`). File paths and file names always render left-to-right inside RTL layouts.

## Engineering standards

- C# latest for .NET 10, `Nullable` enabled, warnings as errors, analyzers on, `Microsoft.CodeAnalysis.BannedApiAnalyzers` configured to forbid file-mutation APIs outside the data-directory abstraction.
- Every public service has an interface; every model/OCR/vector engine sits behind an interface so bake-off winners are config changes.
- Async with `CancellationToken` everywhere; no blocking on async; no `Console.WriteLine` for logging.
- Pure logic (normalizer, stemmer, classifier, quality gate, ranking, validators) is developed **test-first**.
- Native/untrusted parsing (PDF, Office, image decode, OCR) runs in the **worker process** with limits, never in the UI process.
- Settings come from options classes, never hard-coded paths or model names.

## Workflow

- Work one task at a time, in plan order. Small commits, message format `M<n>-T<id>: summary`.
- Before saying a task is done run `tools\check.ps1` (build, tests, analyzers, banned-API check, corpus-integrity test) and paste the summary into `docs/PROGRESS.md`.
- Record every non-trivial decision as `docs/adr/NNNN-title.md` (context, options, evidence, decision).
- Time-box experiments (bake-offs: max 2 working days each). If a time-box expires, record what you have and ask.
- When blocked or when a **gate** in the plan is reached, stop, summarize evidence, list options with a recommendation, and wait.
- Keep `docs/risks.md` current: add new risks when you discover them, mark mitigated ones with evidence.
