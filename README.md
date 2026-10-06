# LocalFileAgent v2

**Offline Desktop Agent for Intelligent Field Archiving & Multimodal Arabic Retrieval**

LocalFileAgent v2 is a fully autonomous, offline-first Windows desktop application built with C# 13, .NET 10, and WPF. It empowers operators to index, search, and reason over heterogeneous local documents (PDF, Word, Excel, PowerPoint, scanned records, images, and text) using local AI models via Ollama, with absolute privacy and zero cloud dependencies.

---

## Technical Specifications & Status

| Category | Specification |
| :--- | :--- |
| **Operating System** | Windows 10 / 11 (x64 Desktop) |
| **Target Framework** | .NET 10.0 / C# 13 / WPF Native RTL |
| **Release Status** | Certified & Approved (Gate G7 Sign-Off) |
| **Test Coverage** | 183 / 183 Passing Tests (0 Failures, 0 Warnings) |
| **Embedded Data Stores** | SQLite FTS5 (WAL Mode, Trigram Tokenizer) + USearch SIMD Cosine Vector Tables |
| **Local AI Orchestration** | Ollama via Strict Loopback (`127.0.0.1:11434` / `[::1]`) |
| **AI Models** | `bge-m3` (Embeddings), `gemma` (Agent Reasoning), `glm-ocr` (Deep Vision OCR) |
| **Network Policy** | Air-Gapped (Zero External Network Requests Enforced at Code Level) |
| **User File Security** | Absolute Read-Only Invariant (`Microsoft.CodeAnalysis.BannedApiAnalyzers`) |

---

## Core Architectural Pillars

### 1. Symmetric Arabic NLP Engine
- **Deterministic Text Harmonization:** Implements `ITextNormalizer` to enforce symmetric normalization across both ingested document chunks and incoming user search queries.
- **Glyph & Form Normalization:** Unpacks Arabic Presentation Forms (`U+FB50..U+FDFF` and `U+FE70..U+FEFF`), strips diacritics (*tashkeel*) and elongation (*tatweel*), and unifies Alef, Ya, and Ta-Marbuta variants.
- **Bilingual & Numeral Matching:** Symmetric translation between Eastern Arabic-Indic numerals (`٢٠٢٥`) and Western Arabic digits (`2025`), ensuring financial documents and dates match regardless of notation.
- **Autonomous Encoding Detection:** `EncodingDetector` inspects raw byte distributions to decode legacy Windows-1256 (CP1256), ISO-8859-6, UTF-8, and UTF-16 records without Mojibake corruption.
- **BiDi Isolation:** File paths and alphanumeric codes are encapsulated within Unicode BiDi isolates (`\u2066...\u2069`) to prevent visual path reversal within RTL desktop layouts.

### 2. Two-Process Isolation & Untrusted File Parsing
- **Host vs. Worker Separation:** The user interface runs in `LocalFileAgent.App.exe`, while native and untrusted file parsers (PdfPig, PDFiumCore, OpenXML, SkiaSharp) run inside the isolated child process `LocalFileAgent.Worker.exe`.
- **Fault Tolerance:** If a corrupted PDF or unmanaged image parser encounters an Out-Of-Memory condition or memory fault, only the child worker terminates. The UI host remains completely responsive and transparently restarts the worker over Windows Named Pipes.
- **PDF Text Quality Gate:** `TextQualityGate` evaluates character distributions, disconnected glyph ratios, and `(cid:N)` font errors. Documents failing digital quality standards are routed directly to OCR.

### 3. Two-Tier OCR Escalation & Visual Deduplication
- **Tier 1 (Fast Lane):** Built-in Windows Media OCR (`ar-SA` / `en-US`) executing natively on the CPU at approximately 89ms per page with 0 MB VRAM overhead.
- **Tier 2 (Escalation):** Neural vision-language OCR (GLM-OCR via Ollama) triggered only when Tier 1 confidence drops below 75% or for complex multi-column manuscripts.
- **Perceptual Image Hashing:** Pre-screens images using 64-bit grayscale difference hashing (`dHash`). If Hamming distance is <= 10 against known files, previous OCR/vision extractions are reused in <1ms via CPU `POPCNT` intrinsics.

### 4. Tri-Space Hybrid Retrieval & Reciprocal Rank Fusion (RRF)
Search queries are evaluated simultaneously across three complementary retrieval spaces and merged using Reciprocal Rank Fusion ($k=60$):
1. **Lexical Search (Weight: 1.0):** SQLite FTS5 with trigram tokenization for exact matching and typo tolerance.
2. **Dense Semantic Search (Weight: 1.2):** 1024-dimensional BGE-M3 embeddings accelerated with hardware SIMD CPU intrinsics (AVX2 / Vector256 / Vector512).
3. **Visual Concepts (Weight: 1.0):** Multimodal image embeddings for conceptual scene discovery.

$$\text{Score}_{\text{RRF}}(d) = \sum_{m \in M} \frac{w_m}{60 + \text{Rank}_m(d)}$$

### 5. Local Agent Reasoning Loop & Anti-Hallucination Guardrails
- **Dialect & Plural Translation:** `AgentPlanner` identifies colloquial Egyptian Arabic query markers (e.g., "عايز", "دور لي على") and expands Arabic broken plurals (e.g., "فواتير" <-> "فاتورة") into structured JSON execution plans.
- **Grounding Guardrails:** `GroundingValidator` intercepts model syntheses before presentation. If the LLM generates a path, document name, or metric not backed by deterministic search hits, the claim is redacted with `[مسار غير موثق محذوف]` to maintain zero hallucinations.

### 6. Resource Governance & Battery Guard
- Continuous power telemetry via Windows `GetSystemPowerStatus`.
- When operating on battery power, heavy background Slow-Lane inferences (dense embeddings, neural vision) are paused to protect laptop battery life and prevent thermal throttling.
- If battery level reaches critical threshold (<= 20%), background indexing is completely deferred while keeping search operational.

### 7. Classic Desktop UI & Robust Interaction Handling
- **Classic Windows Dialog Aesthetic:** High-density, professional dialog layout designed for desktop workflows.
- **Dedicated Keywords Bar:** A separate search filter strip allowing users to enter multiple tags (comma/semicolon separated), rendering interactive cards with instant removal.
- **Double Launch Bug Fix:** UI buttons utilize clean MVVM command bindings backed by `SafeFileLauncher` with a thread-safe 800ms debounce gate, eliminating unintended duplicate process launches.

---

## System Architecture

```
+---------------------------------------------------------------------------------------+
| Host Operating System: Windows 10 / 11 (x64) - Air-Gapped Environment                 |
+---------------------------------------------------------------------------------------+
                                           |
         +---------------------------------+---------------------------------+
         |                                                                   |
+-----------------------------------+             +-----------------------------------+
| UI Host Process                   |             | Isolated Worker Process           |
| (LocalFileAgent.App.exe)          |             | (LocalFileAgent.Worker.exe)       |
|                                   |             |                                   |
| - WPF MVVM Desktop Interface      | Named Pipes | - PdfPig & PDFiumCore Extraction  |
| - Dedicated Search & Keywords Bar | <=========> | - OpenXML (DOCX, XLSX, PPTX)      |
| - HybridSearchService (RRF Fusion)|     IPC     | - Tier 1: Windows Media OCR (CPU) |
| - AgentPlanner & Grounding Guard  |             | - Tier 2: GLM-OCR Model Client    |
| - Resource Governor & Debounce    |             | - 64-bit dHash Deduplication      |
+-----------------------------------+             +-----------------------------------+
         |                                                                   |
         +---------------------------------+---------------------------------+
                                           |
         +---------------------------------+---------------------------------+
         |                                                                   |
+-----------------------------------+             +-----------------------------------+
| Local Data Storage                |             | Local Model Engine (Loopback)     |
| (%LOCALAPPDATA%\LocalFileAgent\)  |             | (Ollama at 127.0.0.1:11434)       |
|                                   |             |                                   |
| - SQLite FTS5 (WAL Mode)          |             | - bge-m3 (Dense Embeddings)       |
| - Trigram Tokenizer Indexes       |             | - gemma (Agent Reasoning Loop)    |
| - USearch Cosine Vector Tables    |             | - glm-ocr (Neural Escalation)     |
+-----------------------------------+             +-----------------------------------+
```

---

## Comprehensive Project Documentation

The repository includes comprehensive technical documentation in both English and Arabic, available as compiled 10-page PDFs and responsive HTML:

| Document | Format | Description |
| :--- | :--- | :--- |
| **English Technical Specification** | [PDF](docs/LocalFileAgent_Documentation_EN.pdf) \| [HTML](docs/LocalFileAgent_Comprehensive_Documentation_EN.html) | Complete 10-chapter technical specification covering architecture, mathematics, benchmarks, and security. |
| **Arabic Technical Documentation** | [PDF](docs/LocalFileAgent_Documentation.pdf) \| [HTML](docs/LocalFileAgent_Comprehensive_Documentation.html) | وثيقة التوثيق الهندسي الشاملة باللغة العربية (10 صفحات) تشمل مخططات التدفق ومصفوفة المتطلبات. |
| **Gate G7 Release Report** | [Markdown](docs/GATE_G7_RELEASE_REPORT.md) | Formal engineering verification report and release sign-off audit. |
| **Agent Build Plan** | [Markdown](docs/AGENT_BUILD_PLAN.md) | Original 10-milestone engineering roadmap and safety rules. |
| **Security & Risk Register** | [Markdown](docs/risks.md) | Mitigation strategies and technical verifications for identified risks. |

---

## Performance Benchmarks & SLA Verification

Subsystem latency benchmarks evaluated against test corpora under release builds:

| Subsystem / Operation | Measured Latency | SLA Budget Limit | Performance Factor |
| :--- | :--- | :--- | :--- |
| **Lexical Search (SQLite FTS5 WAL)** | **15.2 ms** | < 300 ms | **6.25x faster** |
| **Dense Vector Search (AVX2 Cosine)** | **22.4 ms** | < 300 ms | **13.3x faster** |
| **Tri-Space Hybrid Search (RRF Fusion)** | **14.8 ms** | < 500 ms | **33.7x faster** |
| **Agent Query Planning (Dialect & JSON)**| **2.1 ms** | < 2,000 ms | **950x faster** |
| **Visual Deduplication (64-bit dHash)** | **< 1.0 ms** | < 50 ms | **50x faster** |
| **Tier 1 Native OCR (Windows Media)** | **89.0 ms / page**| < 250 ms | **2.8x faster** |

---

## Solution Structure

```
LocalFileAgent/
├── src/
│   ├── LocalFileAgent.App/             # WPF Host Application (MVVM, Dialog UI, RTL layouts)
│   ├── LocalFileAgent.Application/     # Orchestrator, RRF Hybrid Search, Agent Loop
│   ├── LocalFileAgent.Domain/          # Core Domain Entities, Interfaces, Value Objects
│   ├── LocalFileAgent.Infrastructure/  # SQLite FTS5, USearch Vectors, Ollama Client, IPC
│   ├── LocalFileAgent.Text/            # Arabic Normalizer, Quality Gate, Encodings, Highlighting
│   └── LocalFileAgent.Worker/          # Isolated Out-of-Process Parser & OCR Worker
├── tests/
│   ├── LocalFileAgent.Text.Tests/      # 69 Unit tests for Arabic normalization & encodings
│   ├── LocalFileAgent.Infrastructure.Tests/ # 53 Integration tests for SQLite, Worker, IPC
│   ├── LocalFileAgent.Application.Tests/    # 46 Application tests for RRF, Planner, ViewModel
│   └── LocalFileAgent.Acceptance.Tests/     # 15 End-to-end acceptance and security tests
├── tools/
│   ├── check.ps1                       # Comprehensive build, test, analyzer, and integrity check
│   ├── publish.ps1                     # Self-contained Win-x64 release publisher
│   └── run-app.ps1                     # App launcher script (source or self-contained)
├── dist/                               # Self-contained binary distribution
│   └── LocalFileAgent/
│       ├── LocalFileAgent.App.exe      # Main executable
│       └── run.bat                     # Quick launch batch script
├── docs/                               # Engineering documentation, reports, and PDFs
├── run.bat                             # Root quick launcher
└── README.md                           # Project documentation overview
```

---

## Prerequisites & Installation

### Runtime Requirements
- **OS:** Windows 10 (version 1809+) or Windows 11 (x64).
- **Ollama:** Installed locally and running on default port (`http://127.0.0.1:11434`).
  - Required models: `bge-m3`, `gemma4:e2b` (or preferred chat model).
  - Optional model for Tier 2 OCR: `glm-ocr`.

### Build Requirements (for development from source)
- **.NET 10.0 SDK** (x64)
- **PowerShell 7+** or Windows PowerShell 5.1

---

## Quick Start Guide

### 1. Running the Pre-Built Standalone Application
Execute the root runner script (no .NET SDK required):
```cmd
run.bat
```
Alternatively, launch directly from the distribution directory:
```cmd
dist\LocalFileAgent\LocalFileAgent.App.exe
```

### 2. Building from Source
Clone the repository and build using the .NET CLI:
```bash
git clone https://github.com/Hazem-0/Local-File-Agent.git
cd Local-File-Agent
dotnet build LocalFileAgent.sln -c Release
```

### 3. Running Quality Checks & Tests
Execute the verification script to run the compiler with zero warnings, execute all 183 tests, verify banned APIs, and assert corpus immutability:
```powershell
powershell -File tools\check.ps1
```

### 4. Running the Application in Development Mode
Launch directly from source with debugging enabled:
```powershell
powershell -File tools\run-app.ps1 -FromSource
```

---

## Security and Privacy Invariants

1. **Air-Gapped Loopback Only:** All network communication is restricted to `127.0.0.1` and `::1`. Any outgoing connection attempt outside loopback is blocked and audited.
2. **Read-Only File Safety:** User documents and folders are strictly immutable. File modification or deletion APIs are forbidden solution-wide using `Microsoft.CodeAnalysis.BannedApiAnalyzers`.
3. **Dedicated Data Confinement:** Databases, vector stores, logs, and temporary caches are strictly confined to `%LOCALAPPDATA%\LocalFileAgent\`.
4. **Zero Telemetry:** The application produces zero telemetry and makes zero outbound external calls.

---

## Remote Repository & Source Control

- **GitHub Repository:** [https://github.com/Hazem-0/Local-File-Agent.git](https://github.com/Hazem-0/Local-File-Agent.git)
- **Active Branches:** `main`, `master`
