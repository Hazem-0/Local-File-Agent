# Risk Register

Updated per milestone. Status: `Open` | `Mitigated` | `Accepted`.

## Arabic and Language Risks

| ID | Risk | Likelihood | Impact | Status | Mitigation / Evidence |
|---|---|---|---|---|---|
| R01 | Arabic PDF text layers reversed, disconnected, or garbage | H | H | Open | NFKC, order fixer, quality gate, two PDF text engines (PdfPig + PDFium), OCR fallback, test fixtures. |
| R02 | Arabic OCR weak; small VLMs hallucinate or loop | H | H | Open | Two-tier OCR with quality gate (Windows OCR -> GLM-OCR), hallucination guards, noise-tolerant trigram/vector retrieval. |
| R03 | Windows OCR Arabic pack missing/unverified | M | H | Mitigated | Verified in M0 probe: `ar-SA` and `en-US` recognizers are installed and available. |
| R04 | FTS tokenizer mishandles tashkeel/tatweel/digits | H | M | Open | Normalize in C# before FTS; tests AR-1/AR-2. |
| R05 | Over-normalization or stemming raises false positives | M | M | Open | Keep ة/ؤ/ئ normalization off by default; stemming is separate low-weight channel; ablate in M6. |
| R06 | Dialect/Arabizi queries mis-planned | M | M | Open | Deterministic fallback analyzer; query variants; evaluate agent model on dialect set. |
| R07 | Cross-lingual mismatch (Arabic <-> English) | M | M | Open | Evaluate embedder cross-lingual metrics in M3; query translation variants. |
| R08 | Token-limit assumptions wrong -> silent truncation | M | M | Open | Character chunking (1,000-1,600 chars) + measured embedder token limit assertions. |
| R09 | Mojibake in CP1256 / ISO-8859-6 text and CSV | H | M | Open | Encoding detector + Arabic letter ratio heuristic + byte fixtures in M1. |
| R10 | Bidi bugs in UI (paths, mixed text, highlights) | H | L-M | Open | Unicode isolate wrappers (U+2066..U+2069) for paths, offset maps, per-item direction, manual RTL review. |
| R11 | SigLIP 2 weak with direct Arabic queries | M | L | Open | Translate visual queries to English via planner; caption-based retrieval. |
| R12 | Arabic file names: NFC/NFD, very long paths, mixed scripts | M | M | Open | Normalize names for search; long-path manifest; unit tests. |
| R13 | Legacy .doc/.xls common and unsupported by Open XML | H | M | Open | M5b sandboxed LibreOffice conversion if installed, else metadata only with clear UI note. |
| R14 | OCR/caption time for large archives is long | H | M | Open | Two-lane indexing, priorities, hash caching, page caps (50 default), visual budgets. |

## Security, Privacy, Safety Risks

| ID | Risk | Likelihood | Impact | Status | Mitigation / Evidence |
|---|---|---|---|---|---|
| R15 | Prompt injection through documents, OCR, captions | M | H | Open | Treat text as untrusted data, read-only schema tools, no tools in OCR/caption calls, grounding validator, injection test suite. |
| R16 | Decompression bombs, malformed files, native crashes | M | H | Open | Worker process with Job Object limits, pixel/page/time caps, zip-ratio checks, crash recovery. |
| R17 | Hallucinated captions or OCR presented as fact | M | M | Open | Explicit source labels (OCR/caption/live vision), confidence scores, "model-read" tag. |
| R24 | Accidental modification of user files | L | H | Open | Banned-API analyzer, read-only handles, corpus-integrity test in every test run. |
| R25 | Sensitive data (IDs, credentials) in index/logs | M | H | Open | Optional DPAPI encryption, private folder exclusions, no document text in logs, redaction options. |
| R26 | Private corpus leaked | L | H | Open | AGENTS.md rule 3, git-ignored `corpus/private/`, metrics-only reporting, no network in product. |
| R27 | Ollama exposed beyond loopback or port hijacked | L | H | Open | Loopback guard in code, verify 127.0.0.1 at startup and per request. |
| R28 | Index corruption or crash inconsistency | M | M | Open | WAL mode, vector generations, rollback to previous valid generation. |
| R39 | OneDrive/cloud placeholders trigger network downloads | M | H | Open | Skip files with recall-on-data-access/offline attributes; counter + UI note. |

## Models, Runtime, Performance Risks

| ID | Risk | Likelihood | Impact | Status | Mitigation / Evidence |
|---|---|---|---|---|---|
| R18 | Ollama/llama.cpp updates break models | M | M | Open | Pin version; model smoke tests after updates; both candidates behind ILocalChatModel. |
| R19 | RAM/VRAM exhaustion with multiple resident models | M | H | Open | Lite Profile (E2B), OLLAMA_MAX_LOADED_MODELS=1, short keep_alive, interactive vs indexing modes. |
| R20 | Model/library licenses restrict redistribution | M | M | Open | Review model cards before bundling; Ollama-managed install for MVP. |
| R21 | Invalid or unsafe plan JSON | M | M | Open | JSON schema format parameter, C# schema validator, single retry, deterministic fallback. |
| R22 | Embedding model change forces full re-embed | M | M | Open | Vector generations, background rebuild, UI notification. |
| R23 | ANN with restrictive scope filters returns few results | M | M | Open | Exact brute-force for scopes <50,000 chunks, over-fetch for large scopes. |
| R37 | Index size growth | M | M | Open | Capacity report in UI, trigram only for OCR chunks, LRU thumbnail cache. |
| R38 | Antivirus/Defender scanning slows or locks files | M | L-M | Open | Backoff and retry on sharing violations; measure; document exclusions as user choice. |
| R41 | Laptop battery/thermal impact of background OCR | M | M | Open | Pause on battery, CPU throttling, quiet hours. |
