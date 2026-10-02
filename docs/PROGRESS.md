# Progress Tracking

## M0 — Environment and repository bootstrap (2026-10-02)
**Status:** done
**Tasks done:**
- T0.1 Probe environment: `tools/probe-env.ps1` and `tools/OcrProbe` created and executed. Generated `docs/environment.md` confirming Windows 11 Build 26220, RTX 3050 (4 GB VRAM), 15.6 GB RAM, Ollama 0.35.0, and native WinRT OCR support for `ar-SA` and `en-US`.
- T0.2 Solution skeleton: 10 projects in `LocalFileAgent.sln`, pinned .NET 10 SDK in `global.json`, enforced `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `Microsoft.CodeAnalysis.BannedApiAnalyzers` configured with `BannedSymbols.txt` forbidding file mutations.
- T0.3 Tooling: `tools/check.ps1` created, `.gitignore` protects private corpus and build outputs, risk register initialized in `docs/risks.md`.
- T0.4 SQLite capability test: `SqliteCapabilityTests` verifies WAL mode, FTS5 with `unicode61`, and FTS5 with `trigram` tokenizer against Arabic text.
- T0.5 Corpus-integrity test: `CorpusIntegrityTests` with `CorpusIntegritySnapshot` helper asserts zero file modifications during read-only operation.

**Evidence:**
- Build output: `0 Warning(s)`, `0 Error(s)`.
- Test run: 6 tests executed, 6 passed (0 failed).
- OCR probe: `ar-SA` supported: True, `en-US` supported: True, `MaxImageDimension: 10000`.
- Automated check: `tools/check.ps1` returned exit code 0 (`ALL CHECKS PASSED`).

**Decisions:**
- [ADR 0001](adr/0001-lite-profile-and-two-tier-ocr.md): Lite profile adopted for 4 GB VRAM budget; Two-Tier OCR (Windows OCR -> GLM-OCR); Hybrid dual vector space retrieval (SQLite FTS5 + USearch HNSW + BGE-M3 + SigLIP 2); RTL Arabic UI with LTR Unicode isolates for paths.

**New/changed risks:**
- R03 (Windows OCR Arabic pack): Marked `Mitigated` with evidence from `OcrProbe`.

**Gate G0 Status:**
- Complete and verified. Ready for Gate G1 review (approving model pull list before M3).

**Next:**
- Begin **Milestone M1 — Arabic text core**:
  - T1.1 Normalizer (`ITextNormalizer` with NFKC, tashkeel/tatweel stripping, digit normalization, offset mapping)
  - T1.2 Encoding detector (`IEncodingDetector` with CP1256, ISO-8859-6, UTF-16 heuristics)
  - T1.3 Order fixer & text quality gate (`IArabicOrderFixer`, `ITextQualityGate`)
  - T1.4 Light stemmer (`IArabicStemmer` with Larkey light10)
  - T1.5 Structure & character-bounded Chunker (`IChunker`)
  - T1.6 Query analyzer (`IQueryAnalyzer`)
