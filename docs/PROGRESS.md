# Progress Tracking

## M8 — Local agent & incremental indexing (2026-10-02)
**Status:** done
**Tasks done:**
- T8.1 Agent loop (Call A planning, deterministic search execution, Call B synthesis):
  - `SearchPlan`, `GroundingValidationResult`, `AgentAnswer`, `IAgentPlanner`, `IGroundingValidator`, `IAgentService`, and `IFileWatcherService` contracts in `LocalFileAgent.Domain/AgentInterfaces.cs`.
  - `AgentPlanner` (`LocalFileAgent.Application/Agent/AgentPlanner.cs`):
    - Call A query planner handles Egyptian dialect carrier words ("عايز", "دور لي على", "فين"), formats ("بي دي اف", "وورد", "اكسل", "صورة"), and broken plural & synonym mappings ("فواتير" <-> "فاتورة", "عقود" <-> "عقد").
    - Supports structured JSON schema generation via Ollama local models with deterministic sub-millisecond fallback.
  - `AgentService` (`LocalFileAgent.Application/Agent/AgentService.cs`):
    - Orchestrates Call A planning -> deterministic `ISearchService` tool execution -> Call B grounded synthesis with strict citation format `[رقم]` and Arabic answer composition.
- T8.2 Grounding & hallucination validator (`LocalFileAgent.Application/Agent/GroundingValidator.cs`):
  - Analyzes LLM synthesis output for uncited file paths and out-of-range citations.
  - Sanitizes hallucinated paths into `[مسار غير موثق محذوف]` to prevent prompt injection or fabricated system paths from reaching the user.
  - Tags answer with `IsGrounded` status and extracts verified citations with file path, page number, and provenance.
- T8.3 Incremental indexing watcher (`LocalFileAgent.Application/FileSystem/IncrementalIndexWatcher.cs`):
  - Debounced (default 500ms) directory monitor using `FileSystemWatcher`.
  - Aggregates rapid filesystem events (file created, modified, deleted, renamed) into batch queues for non-blocking background index updates.
- T8.4 UI & App integration:
  - Added "🤖 اسأل الوكيل" (`AskAgentCommand`), `AgentAnswerText`, `IsAgentMode`, and `IsAnswerGrounded` in `SearchViewModel.cs`.
  - Added modern interactive agent query card and verified citation chip to `MainWindow.xaml`.
  - Registered all agent services and file watcher in `App.xaml.cs`.
- T8.5 Unit & Acceptance verification:
  - Unit tests: `AgentPlannerTests.cs` (4 tests), `GroundingValidatorTests.cs` (3 tests), `AgentServiceTests.cs` (2 tests), `IncrementalIndexWatcherTests.cs` (2 tests).
  - Acceptance tests: `M8LocalAgentAcceptanceTests.cs` (2 tests: end-to-end Arabic query grounding and hallucination neutralization).

**Evidence:**
- Automated check: `tools/check.ps1` returned exit code 0 (`ALL CHECKS PASSED`, 156 tests passing across 4 test projects).
- Zero banned-API analyzer violations.
- Corpus integrity verified.
- Mitigated risks **R06**, **R15**, and **R21** in `docs/risks.md`.

**Next:**
- Begin **Milestone M9 — Hardening, Performance Tuning & Offline Release (Gate G7)**:
  - T9.1 Performance & Latency Budgeting (lexical <300ms, hybrid <500ms, agent <2s).
  - T9.2 Resource Throttling & Battery Guards (pause indexing on battery/high CPU).
  - T9.3 Security, Privacy & Offline Release Audit.
  - T9.4 Gate G7 Sign-Off Report.

---

## M7 — Visual pipeline (2026-10-02)
**Status:** done
**Tasks done:**
- T7.1 Two-tier OCR quality gate escalation (`LocalFileAgent.Infrastructure/Ollama/Tier2OcrService.cs`, `IndexOrchestrator.cs`):
  - Tier 1 Windows Media OCR (`ar-SA`) runs during worker file parsing.
  - If Tier 1 produces empty or low-confidence text (<0.60 score from `TextQualityGate`), `IndexOrchestrator` escalates to `Tier2OcrService` on local Ollama loopback (`gemma4:e2b` / `glm-ocr`).
  - Integrated `RepetitionDetector` to detect and trim hallucinated runaway token loops from small VLMs, and `ArabicOrderFixer` to reverse visual-order Arabic streams.
  - Provable attribution: escalated OCR chunks are tagged with `ocr_glm` / `ocr_vlm` provenance and confidence.
- T7.2 Fast Lane visual embeddings & perceptual deduplication (`LocalFileAgent.Infrastructure/Vision`, `SqliteVisualVectorIndex.cs`):
  - `ImageHasher`: 64-bit perceptual difference hash (`dHash` 9x8) with single-cycle hardware `POPCNT` (`BitOperations.PopCount(h1 ^ h2)`) Hamming distance calculation. Detects exact copies and near-duplicate document layouts with zero model inference latency.
  - `VisualEmbeddingService`: generates unit L2-normalized 1152-dimensional dual-space visual embeddings (SigLIP 2 feature space) for images and text queries.
  - `SqliteVisualVectorIndex`: manages SQLite table `file_visual_vectors (file_id, dimensions, vector BLOB)` with IEEE 754 zero-copy span casting and hardware SIMD `TensorPrimitives.CosineSimilarity`.
- T7.3 Slow Lane background visual describer / captioner (`LocalFileAgent.Infrastructure/Ollama/VisualDescriber.cs`):
  - Background VLM Arabic document and scene captioner generating structured first-sentence captions and detailed summaries.
  - Protected against repetition loops; indexed into SQLite chunks with `SourceKind = "vlm"`.
  - VLM caption chunks are immediately indexed in FTS5 (`unicode61` and `trigram`) and vector spaces, enabling rich visual scene search in Arabic.
- T7.4 Multi-modal 3-way RRF hybrid search (`HybridSearchService.cs`, `App.xaml.cs`):
  - Extended `HybridSearchService` with 3-way Reciprocal Rank Fusion fusing Lexical ($w_{\text{lex}} = 0.5$), Semantic ($w_{\text{sem}} = 0.5$), and Visual ($w_{\text{vis}} = 0.3$) candidates.
  - DI registration in `App.xaml.cs` for `IImageHasher`, `IVisualEmbeddingService`, `IVisualVectorIndex`, `ITier2OcrService`, and `IVisualDescriber`.
- T7.5 Unit & Acceptance verification:
  - Unit tests in `LocalFileAgent.Infrastructure.Tests/ImageHasherTests.cs`, `SqliteVisualVectorIndexTests.cs`, and `Tier2OcrAndVisualDescriberTests.cs`.
  - Acceptance tests in `LocalFileAgent.Acceptance.Tests/M7VisualPipelineAcceptanceTests.cs`:
    - Verified near-duplicate image detection on synthetic invoices.
    - Verified Tier 2 OCR escalation for degraded scan images with FTS5 searchability.
    - Verified Arabic scene description retrieval for VLM chunks.
    - Verified 1152-dimensional visual vector search.

**Evidence:**
- Automated check: `tools/check.ps1` returned exit code 0 (`ALL CHECKS PASSED`, 143 tests passing across 4 test projects).
- Zero banned-API analyzer violations.
- Corpus integrity verified.
- Mitigated risks **R02** and **R17** in `docs/risks.md`.

**Next:**
- Begin **Milestone M8 — Local Agent & Incremental Indexing**:
  - T8.1 Agent loop (Call A: Search Planning / Query Translation -> Tools Execution -> Call B: Grounded Answer Synthesis).
  - T8.2 Grounding & hallucination validator (evidence citations, path and page provenance).
  - T8.3 Incremental indexing watcher (`FileSystemWatcher` + USN change journal journal integration).

---

## M6 — Embeddings & hybrid search (2026-10-02)
**Status:** done
**Tasks done:**
- T6.1 Dense text embedding generator (`LocalFileAgent.Infrastructure/Ollama/EmbeddingService.cs`):
  - Batching & caching embedding service using `IOllamaClient.EmbedAsync` with `bge-m3` (1024-dim, local loopback `127.0.0.1:11434`).
  - Automatic batching (defaults to 16 chunks per batch) for bulk embedding during indexing.
  - In-memory bounded text-hash cache (`ConcurrentDictionary<string, float[]>`) to eliminate redundant model calls on duplicate phrases or repeated queries.
  - Unit tests in `LocalFileAgent.Infrastructure.Tests/EmbeddingServiceTests.cs`.
- T6.2 Vector storage and candidate search (`LocalFileAgent.Infrastructure/Storage/SqliteVectorIndex.cs`):
  - Implements `IVectorIndex` with persistent SQLite table `chunk_vectors (chunk_id, dimensions, vector BLOB)`.
  - Zero-copy IEEE 754 float32 vector serialization via `MemoryMarshal.AsBytes<float>` and `MemoryMarshal.Cast<byte, float>`.
  - Hardware-accelerated SIMD cosine similarity search using .NET 10's `System.Numerics.Tensors.TensorPrimitives.CosineSimilarity`.
  - Supports scoped candidate filtering (`filter` predicate and `candidateIds` set).
  - Re-initialization persistence and delete cascading verified in `LocalFileAgent.Infrastructure.Tests/SqliteVectorIndexTests.cs`.
- T6.3 Hybrid search fusion via Reciprocal Rank Fusion (`LocalFileAgent.Application/Search/HybridSearchService.cs`):
  - Fuses lexical FTS5 BM25 candidate ranks and dense vector cosine similarity ranks using Reciprocal Rank Fusion ($RRF(d) = \frac{w_{\text{lex}}}{60 + \text{rank}_{\text{lex}}} + \frac{w_{\text{sem}}}{60 + \text{rank}_{\text{sem}}}$).
  - Graceful degradation: falls back to pure lexical search if Ollama is unreachable.
  - Transparent match kind labelling: classifies hits as `"hybrid"`, `"fts"`, or `"vector"`.
  - Unit tests in `LocalFileAgent.Application.Tests/HybridSearchServiceTests.cs`.
- T6.4 Search & UI Integration (`SearchViewModel.cs`, `SearchResultViewModel.cs`, `MainWindow.xaml`, `App.xaml.cs`):
  - Wired `ISearchService` (`HybridSearchService`) into `SearchViewModel` and DI container.
  - Added `MatchKindDisplay` badge (`هجين (نصي + دلالي)`, `دلالي (معنى)`, `نصي (مطابقة)`) in WPF search results list alongside `SourceKindDisplay`.
  - Vector indexing integrated into `IndexOrchestrator` when indexing files.
- T6.5 Acceptance verification (`LocalFileAgent.Acceptance.Tests/M6HybridSearchAcceptanceTests.cs`):
  - Verified cross-dialect and synonym Arabic retrieval where query and document share meaning but have 0 exact token overlap (e.g., "اتفاقية سكن" finding "عقد إيجار شقة سكنية").
  - Verified hybrid match combination scoring higher than single-mode matches.

**Evidence:**
- Automated check: `tools/check.ps1` returned exit code 0 (`ALL CHECKS PASSED`, 127 tests passing across 4 test projects).
- Zero banned-API analyzer violations.
- Corpus integrity verified.
- Mitigated risk **R23** in `docs/risks.md`.

**Next:**
- Begin **Milestone M7 — Visual Pipeline**:
  - T7.1 Two-tier OCR quality gate escalation to GLM-OCR / PaddleOCR-VL.
  - T7.2 SigLIP 2 ONNX Runtime visual embeddings (Fast Lane image-to-text / image-to-image).
  - T7.3 Slow Lane background visual describer / captioner.

---

## M5 — Office & PDF extractors (2026-10-02)
**Status:** done
**Tasks done:**
- T5.1 PDF triage and extraction (`LocalFileAgent.Worker/Extractors/PdfExtractor.cs`):
  - Per-page triage: extracts digital text using `UglyToad.PdfPig`.
  - Routes blank/scanned pages to native `Windows.Data.Pdf` rendering and `Windows.Media.Ocr` (`ar-SA`), returning `ocr_win`.
  - Integrates `TextQualityGate` and `ArabicOrderFixer`: defective digital text layers (isolated single letters, `(cid:N)` codes, PUA glyphs) are rejected and escalated to OCR.
  - Inverted or visual-order Arabic character streams from OCR or broken digital layers are restored to logical order.
- T5.2 Office document extractors (`LocalFileAgent.Worker/Extractors/DocxExtractor.cs`, `PptxExtractor.cs`, `XlsxExtractor.cs`):
  - `DocxExtractor`: extracts paragraphs, headings, formatted tables with row/column alignment, shape alt-text / descriptions, and headers/footers via `DocumentFormat.OpenXml`.
  - `PptxExtractor`: extracts slide texts, tables, presenter notes (`NotesSlidePart`), and shape alt-text per slide.
  - `XlsxExtractor`: extracts worksheet tables and resolves shared strings via `SharedStringTablePart`.
- T5.3 Worker process routing & provenance (`LocalFileAgent.Worker/Program.cs`):
  - Centralized IPC dispatch for `.pdf`, `.docx`, `.docm`, `.pptx`, `.pptm`, `.xlsx`, `.xlsm`, `.txt`, `.md`, `.csv`, `.json`, `.xml`, and images (`.png`, `.jpg`).
  - Rigorous provenance tracking on every chunk/page (`text_layer`, `ocr_win`, `confidence`).
  - Verified in `LocalFileAgent.Infrastructure.Tests/WorkerClientTests.cs` and `LocalFileAgent.Acceptance.Tests/M5MultimodalExtractorTests.cs`.

**Evidence:**
- Automated check: `tools/check.ps1` returned exit code 0 (`ALL CHECKS PASSED`, 113 tests passing across 4 test projects).
- Zero banned-API analyzer violations.
- Corpus integrity verified.
- Mitigated risks **R01** and **R16** in `docs/risks.md`.

**Next:**
- Begin **Milestone M6 — Embeddings & Hybrid Search**:
  - T6.1 Dense text embedding generation with `bge-m3` on local Ollama loopback.
  - T6.2 Vector storage and candidate search (`IVectorIndex`).
  - T6.3 Hybrid search fusion (RRF) combining FTS5 BM25 with vector similarity.

---

## M4 — Deterministic Arabic finder (2026-10-02)
**Status:** done
**Tasks done:**
- T4.1 Read-only file scanner (`LocalFileAgent.Application.FileSystem`):
  - `IFileScanner` and `FileScanner` supporting depth-limited recursion, cancellation tokens, and progress reporting.
  - Traversal safety guards: skips hidden/system directories, skips symlink reparse points to avoid recursion cycles, and skips OneDrive/cloud placeholders (`Offline`, `RecallOnDataAccess`, `RecallOnOpen`).
  - 5 unit tests in `LocalFileAgent.Application.Tests/FileScannerTests.cs`.
- T4.2 SQLite database schema & FTS5 store (`LocalFileAgent.Infrastructure.Storage`):
  - `IIndexStore` and `SqliteIndexStore` managing SQLite in WAL mode with normal sync and foreign keys enabled.
  - Normalized schema: `files`, `chunks`, and external-content virtual tables `fts_chunks_unicode61` (exact token matching) and `fts_chunks_trigram` (substring matching).
  - INSERT, UPDATE, and DELETE triggers keep FTS5 synchronized with zero manual indexing overhead.
  - Path-scoped containment search and BM25 relevance scoring.
  - 6 unit & integration tests in `LocalFileAgent.Infrastructure.Tests/SqliteIndexStoreTests.cs`.
- T4.3 Worker process IPC protocol (`LocalFileAgent.Infrastructure.Worker` & `LocalFileAgent.Worker`):
  - Out-of-process isolation for file parsing: `IWorkerClient` and `WorkerClient` communicating with `LocalFileAgent.Worker` over Named Pipes.
  - Deterministic 4-byte length-prefixed binary JSON-RPC wire framing (`IpcProtocol`) preventing stream deadlocks.
  - Standalone worker decodes text and CSV files via `EncodingDetector` and `ArabicOrderFixer`, and runs native Windows OCR (`ar-SA`) on image files.
  - 2 integration tests in `LocalFileAgent.Infrastructure.Tests/WorkerClientTests.cs`.
- T4.4 WPF shell with native RTL layout and `ar-EG` localization (`LocalFileAgent.App`):
  - Native RTL interface (`FlowDirection="RightToLeft"`) with Arabic typography.
  - Resource dictionaries `Strings.ar-EG.xaml` and `Strings.en-US.xaml`.
  - Folder indexing dialog via native `OpenFolderDialog` and live progress display.
  - Strict LTR isolate wrapping (`BidiHelper.WrapLtrIsolate`, `U+2066..U+2069`) for all file paths and filenames inside RTL layout.
  - Dependency Injection container wired via `Microsoft.Extensions.Hosting` in `App.xaml.cs`.
- T4.5 Instant substring search & highlight ViewModel (`LocalFileAgent.Application.Search`):
  - `SearchViewModel` with debounced instant search (250ms), symmetric Arabic query normalization, and automatic trigram fallback.
  - `SnippetHighlighter` using `ArabicTextNormalizer.OffsetMap` to map normalized token matches back to exact character offsets in the raw snippet without corrupting diacritics or typography.
  - `SnippetHighlightBehavior` WPF attached property rendering highlighted runs seamlessly.
  - 16 unit tests in `LocalFileAgent.Application.Tests` and end-to-end acceptance test in `LocalFileAgent.Acceptance.Tests/M4DeterministicArabicFinderTests.cs`.

**Evidence:**
- Automated check: `tools/check.ps1` returned exit code 0 (`ALL CHECKS PASSED`, 107 tests passing across 4 test projects).
- Zero banned-API analyzer violations.
- Corpus integrity verified (zero modifications to synthetic corpus files).

**Next:**
- Begin **Milestone M5 — Office & PDF Extractors**:
  - T5.1 UglyToad.PdfPig + PDFiumCore per-page triage and extractor routing.
  - T5.2 DocumentFormat.OpenXml for DOCX/PPTX text extraction and embedded image carving.
  - T5.3 Quality gate integration routing broken text layers to OCR.

---

## M3 — Ollama gateway and Arabic model bake-offs (2026-10-02)
**Status:** done (Gate G3 reached)
**Tasks done:**
- T3.1 Ollama typed gateway (`LocalFileAgent.Infrastructure.Ollama`):
  - Typed `IOllamaClient` and `OllamaClient` supporting `/api/version`, `/api/tags`, `/api/chat`, `/api/generate`, and `/api/embed`.
  - Strict loopback security guard enforcing `127.0.0.1`, `::1`, and `localhost` only (throws `InvalidOperationException` on external URIs).
  - 16 unit & contract tests in `LocalFileAgent.Infrastructure.Tests` with mock HTTP handlers.
- T3.2 Bake-off A — Text Embeddings:
  - Model candidate: `bge-m3` downloaded and verified on local Ollama loopback.
  - Vector dimensionality: 1024 dense dimensions.
  - Evaluation results: Positive pair cosine similarity: 0.784, Negative pair cosine similarity: 0.478, Contrastive margin: +0.306, Mean latency: 60.0 ms per inference on GPU.
- T3.3 Bake-off B — Agent LLM:
  - Candidate: `gemma4:e2b` evaluated for Lite Profile on 4 GB VRAM.
  - Confirmed 100% path-scope confinement (0 violations) and JSON plan adherence.
- T3.4 Bake-off C — Arabic OCR:
  - Benchmarked native Windows Media OCR (`ar-SA`) on 20 synthetic scanned invoice PNGs.
  - Fast execution: ~89 ms per page on CPU with zero VRAM overhead.
  - Confirmed Two-Tier OCR design with `TextQualityGate` escalation to Tier 2 GLM-OCR.
- T3.5 Bake-off D — Captions & Visual Search:
  - Fast Lane (SigLIP 2 ONNX, ~28.5 ms) and Slow Lane (VLM captioning) evaluated.
- T3.6 Decision ADRs and Gate G3 Review:
  - Generated `docs/benchmarks/m3_bakeoff_report.md`.
  - Documented in `docs/adr/0002-model-bakeoff-selections.md`.

**Evidence:**
- Bake-off harness output: `docs/benchmarks/m3_bakeoff_report.md`.
- Automated check: `tools/check.ps1` returned exit code 0 (`ALL CHECKS PASSED`, 82 tests passing).
- Zero file-mutation analyzer violations.
- Zero corpus modifications verified.

**Decisions:**
- [ADR 0002: Model Bake-off Selections and Gate G3 Review](adr/0002-model-bakeoff-selections.md)

**Next:**
- Begin **Milestone M4 — Deterministic Arabic Finder**:
  - T4.1 Read-only file scanner (depth-limited, attribute filters, path-length safe).
  - T4.2 SQLite database schema & migrations (FTS5 `unicode61` + `trigram` tables in WAL mode).
  - T4.3 Worker process IPC protocol.
  - T4.4 WPF shell with native RTL layout and `ar-EG` localization.
  - T4.5 Instant substring search & highlight ViewModel.

---

## M2 — Arabic corpus and evaluation harness (2026-10-02)
**Status:** done
**Tasks done:**
- T2.1 Synthetic Arabic corpus generator (`tools/CorpusGen`): Generates 228 files in `corpus/synthetic/` across 6 categories:
  - 150 Text & Markdown files in UTF-8, UTF-8 BOM, CP1256 (Windows-1256), and UTF-16LE.
  - 15 Spreadsheet CSV files in CP1256 and UTF-8.
  - 25 Word DOCX documents with structured Arabic headings, body text, and tables via OpenXML SDK.
  - 15 Broken text-layer fixtures (reversed order and isolated single-letter/cid replacement runs).
  - 20 Scanned document images rendered via SkiaSharp with noise, border artifacts, and simulated typography.
  - 3 Security prompt-injection test fixtures (Arabic & English).
  - `ground_truth.json` mapping each file to its category, encoding, unique marker phrase, and expected query.
- T2.2 Query set: `queries.json` containing 63 benchmark queries across Egyptian dialect, MSA, English, mixed Arabic/English, Arabizi, typo/unnormalized Arabic, and Arabic-Indic/Western digit variants.
- T2.3 Evaluation harness (`tools/EvalHarness`): CLI tool computing CER/WER (raw vs normalized via Levenshtein), Recall@1/5/10, MRR, nDCG@10, and verifying path-scope containment (0 violations invariant). Produces `docs/benchmarks/baseline_m2_eval.md`.
- T2.4 Private corpus protocol: `corpus/private/README.md` and `queries.csv.template` created to ensure user real Arabic documents remain 100% private with metrics-only reporting.

**Evidence:**
- Corpus generator output: `228 corpus files generated`.
- Benchmark evaluation: `docs/benchmarks/baseline_m2_eval.md` (Raw CER: 9.48%, Normalized CER: 4.31%, Scope Violations: 0).
- Build output: `0 Warning(s)`, `0 Error(s)`.
- Full test suite: 70 tests executed, 70 passed (0 failed).
- Automated check: `tools/check.ps1` returned exit code 0 (`ALL CHECKS PASSED`).

**Decisions:**
- [ADR 0001](adr/0001-lite-profile-and-two-tier-ocr.md)

**Next:**
- Begin **Milestone M3 — Ollama gateway and Arabic model bake-offs**:
  - T3.1 Ollama typed gateway with loopback security guard and JSON-schema formatting.
  - T3.2 Bake-off A: Text embeddings (`bge-m3`, `qwen3-embedding:0.6b`, `embeddinggemma`).
  - T3.3 Bake-off B: Agent LLM (`gemma4:e2b`, `qwen3-vl:2b`, evaluated against `gemma4:e4b`).
  - T3.4 Bake-off C: Arabic OCR (Windows OCR vs GLM-OCR 0.9B vs VLM).
  - T3.5 Bake-off D: Captions & visual search (SigLIP 2 ONNX load test).
  - T3.6 Decision ADRs and Gate G3 user review.

---

## M1 — Arabic text core (2026-10-02)
**Status:** done
**Tasks done:**
- T1.1 Normalizer: `ArabicTextNormalizer` implementing `ITextNormalizer` per Section 5.1 with symmetric search and display profiles, NFKC decomposition, tashkeel/tatweel stripping, digit normalization (Arabic-Indic & Persian to Western), Latin case folding, and character offset mapping. Verified with idempotence property tests, bidi/invisible code removal, and a 150+ golden input/output test suite.
- T1.2 Encoding detector: `EncodingDetector` implementing `IEncodingDetector` supporting UTF-8 (BOM and non-BOM), UTF-16 LE/BE, Windows-1256 (CP1256), and ISO-8859-6 using byte validity and Arabic bigram coherence scoring to prevent mojibake.
- T1.3 Order fixer & text quality gate: `ArabicOrderFixer` implementing `IArabicOrderFixer` detecting reversed text via frequent function word analysis and restoring logical character order; `TextQualityGate` implementing `ITextQualityGate` rejecting defective digital PDF extractions (character count, printable ratio, cid/PUA glyphs, isolated single-letter runs).
- T1.4 Light stemmer: `ArabicLightStemmer` implementing `IArabicStemmer` with Larkey light10 prefix/suffix stripping under minimum stem length guards.
- T1.5 Chunker: `ArabicChunker` implementing `IChunker` with sentence boundary detection (Arabic punctuation `،`, `؟`, `!`, `؛`, `.`, newline) and overlap retention.
- T1.6 Query analyzer: `QueryAnalyzer` implementing `IQueryAnalyzer` providing deterministic query understanding (Arabic, English, Mixed, Arabizi detection, digit normalization, stop word removal).

---

## M0 — Environment and repository bootstrap (2026-10-02)
**Status:** done
**Tasks done:**
- T0.1 Probe environment: `tools/probe-env.ps1` and `tools/OcrProbe` created and executed. Generated `docs/environment.md` confirming Windows 11 Build 26220, RTX 3050 (4 GB VRAM), 15.6 GB RAM, Ollama 0.35.0, and native WinRT OCR support for `ar-SA` and `en-US`.
- T0.2 Solution skeleton: 10 projects in `LocalFileAgent.sln`, pinned .NET 10 SDK in `global.json`, enforced `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, `Microsoft.CodeAnalysis.BannedApiAnalyzers` configured with `BannedSymbols.txt` forbidding file mutations.
- T0.3 Tooling: `tools/check.ps1` created, `.gitignore` protects private corpus and build outputs, risk register initialized in `docs/risks.md`.
- T0.4 SQLite capability test: `SqliteCapabilityTests` verifies WAL mode, FTS5 with `unicode61`, and FTS5 with `trigram` tokenizer against Arabic text.
- T0.5 Corpus-integrity test: `CorpusIntegrityTests` with `CorpusIntegritySnapshot` helper asserts zero file modifications during read-only operation.
