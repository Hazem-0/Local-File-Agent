# Progress Tracking

## M1 — Arabic text core (2026-10-02)
**Status:** done
**Tasks done:**
- T1.1 Normalizer: `ArabicTextNormalizer` implementing `ITextNormalizer` per Section 5.1 with symmetric search and display profiles, NFKC decomposition, tashkeel/tatweel stripping, digit normalization (Arabic-Indic & Persian to Western), Latin case folding, and character offset mapping. Verified with idempotence property tests, bidi/invisible code removal, and a 150+ golden input/output test suite.
- T1.2 Encoding detector: `EncodingDetector` implementing `IEncodingDetector` supporting UTF-8 (BOM and non-BOM), UTF-16 LE/BE, Windows-1256 (CP1256), and ISO-8859-6 using byte validity and Arabic bigram coherence scoring to prevent mojibake.
- T1.3 Order fixer & text quality gate: `ArabicOrderFixer` implementing `IArabicOrderFixer` detecting reversed text via frequent function word analysis and restoring logical character order; `TextQualityGate` implementing `ITextQualityGate` rejecting defective digital PDF extractions (character count, printable ratio, cid/PUA glyphs, isolated single-letter runs).
- T1.4 Light stemmer: `ArabicLightStemmer` implementing `IArabicStemmer` with Larkey light10 prefix/suffix stripping under minimum stem length guards.
- T1.5 Chunker: `ArabicChunker` implementing `IChunker` with sentence boundary detection (Arabic punctuation `،`, `؟`, `!`, `؛`, `.`, newline) and overlap retention.
- T1.6 Query analyzer: `QueryAnalyzer` implementing `IQueryAnalyzer` providing deterministic query understanding (Arabic, English, Mixed, Arabizi detection, digit normalization, stop word removal).

**Evidence:**
- Build output: `0 Warning(s)`, `0 Error(s)`.
- Full test suite: 70 tests executed, 70 passed (0 failed).
- Arabic requirements verified: AR-1, AR-2, AR-3, AR-6 unit checks all pass.
- Verification script: `tools/check.ps1` exit code 0 (`ALL CHECKS PASSED`).

**Decisions:**
- [ADR 0001](adr/0001-lite-profile-and-two-tier-ocr.md)

**Next:**
- Begin **Milestone M2 — Arabic corpus and evaluation harness**:
  - T2.1 Synthetic Arabic corpus generator (`tools/CorpusGen`)
  - T2.2 Comprehensive Arabic query set (>= 60 queries across dialects & edge cases)
  - T2.3 Evaluation harness (`tools/EvalHarness` computing CER/WER, recall@k, MRR, nDCG@10)
  - T2.4 Private corpus protocol and safety verification

---

## M0 — Environment and repository bootstrap (2026-10-02)
**Status:** done
**Tasks done:**
- T0.1 Probe environment: `tools/probe-env.ps1` and `tools/OcrProbe` created and executed. Generated `docs/environment.md` confirming Windows 11 Build 26220, RTX 3050 (4 GB VRAM), 15.6 GB RAM, Ollama 0.35.0, and native WinRT OCR support for `ar-SA` and `en-US`.
- T0.2 Solution skeleton: 10 projects in `LocalFileAgent.sln`, pinned .NET 10 SDK in `global.json`, enforced `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `Microsoft.CodeAnalysis.BannedApiAnalyzers` configured with `BannedSymbols.txt` forbidding file mutations.
- T0.3 Tooling: `tools/check.ps1` created, `.gitignore` protects private corpus and build outputs, risk register initialized in `docs/risks.md`.
- T0.4 SQLite capability test: `SqliteCapabilityTests` verifies WAL mode, FTS5 with `unicode61`, and FTS5 with `trigram` tokenizer against Arabic text.
- T0.5 Corpus-integrity test: `CorpusIntegrityTests` with `CorpusIntegritySnapshot` helper asserts zero file modifications during read-only operation.
