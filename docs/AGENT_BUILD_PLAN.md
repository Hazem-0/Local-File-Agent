# LocalFileAgent — Arabic-First Build Plan for the Coding Agent

**Date:** 2026-10-02
**Platform:** Windows 10/11, single user, offline, read-only; C# / .NET 10 / WPF; Ollama already installed on the user's PC
**Relationship to earlier docs:** the v1 design and the v2 multimodal plan remain the architecture reference (pipeline, schema, tool contracts). **This document overrides them wherever they conflict**, mainly because the content is **mostly Arabic**.

---

## 0. How to use this document

1. Read `AGENTS.md` (standing rules), then this file top to bottom once.
2. Execute milestones **in order**: M0 → M9. Milestones M0–M3 are gates: they establish the environment, the Arabic text core, the test corpus, and the measured model choices. Do not build features on unmeasured assumptions.
3. After each milestone: update `docs/PROGRESS.md`, `docs/risks.md`, and ADRs; then **stop for review** if the milestone ends in a gate (Section 9).
4. If something here turns out to be wrong, say so with evidence and propose a change. Do not silently deviate.

---

## 1. Mission and Arabic-first requirements

Build a desktop agent that lets the user find local files by asking in **Arabic (Modern Standard and Egyptian dialect), English, or mixed**, over **Arabic-dominant content**: born-digital PDFs, scanned PDFs, PDFs mixing text and images, Word files (including legacy `.doc`), images/screenshots/photos of documents, and plain text/CSV (possibly in legacy Arabic encodings).

### Requirements (all must be testable)

| ID | Requirement |
|---|---|
| AR-1 | Searching `الفاتورة`, `فاتوره`, `الفَاتُورَة`, and `ﺍﻟﻔﺎﺗﻮﺭﺓ` (presentation forms) finds the same documents (normalization is symmetric on index and query). |
| AR-2 | Arabic-Indic digits (`٢٠٢٥`), Persian digits, and Western digits match each other (e.g., invoice numbers, dates, phone numbers). |
| AR-3 | An Arabic **born-digital PDF whose text layer is broken** (reversed order, disconnected letters, `(cid:N)`, private-use glyphs) is detected and routed to OCR instead of indexing garbage. |
| AR-4 | **Scanned Arabic PDFs and Arabic images** are searchable by their OCR text, with a visible confidence label, and search tolerates OCR errors. |
| AR-5 | Arabic **file and folder names** (including long paths, mixed Arabic/English names) are searchable by substring and with typo tolerance. |
| AR-6 | Text/CSV files in **UTF-8, UTF-8 BOM, UTF-16, Windows-1256 (CP1256), and ISO-8859-6** are decoded correctly without mojibake. |
| AR-7 | Queries in Egyptian dialect, MSA, English, mixed, or transliterated Arabic ("Arabizi") produce a correct validated search plan. |
| AR-8 | Cross-lingual retrieval works both ways: Arabic query finds English documents and the reverse (measured, not assumed). |
| AR-9 | Answers are written in the user's query language (Arabic by default for Arabic queries); file paths and IDs remain left-to-right and copyable. |
| AR-10 | The UI supports **RTL layout**, Arabic localization, Arabic keyboard-only use, and screen readers; result text with mixed directions renders correctly with correct highlighting. |
| AR-11 | Legacy `.doc` / `.xls` / `.ppt` files are indexed (or clearly reported as not indexed with the reason). |
| AR-12 | Everything above works **offline**, read-only, and without exposing inaccessible paths. |

### Non-goals (unchanged)

Cloud/telemetry/web search; any file mutation; face recognition; video/audio understanding; encrypted/password-protected files; perfect handwriting OCR; Quranic/diacritized-manuscript OCR (best effort only).

---

## 2. Technology Stack & Key Decisions

- Runtime: C# on .NET 10
- UI: WPF (MVVM with CommunityToolkit.Mvvm) with native RTL and localization (`ar-EG`, `en-US`)
- Database: SQLite (Microsoft.Data.Sqlite) with FTS5 (`unicode61` + `trigram`) in WAL mode
- Vectors: USearch (HNSW) dual-space (text + visual) with exact brute-force fallback for small scopes (<50,000 chunks)
- Parsing: UglyToad.PdfPig + PDFiumCore for PDF, DocumentFormat.OpenXml for Office
- Images: SkiaSharp + MetadataExtractor + Windows OCR
- Models (Ollama loopback 127.0.0.1:11434):
  - Agent + Vision: Gemma 4 E2B (Lite profile) / E4B (evaluated in M3 bake-off)
  - Text Embeddings: BGE-M3 (default candidate)
  - OCR Tier 1: Windows OCR (`ar-SA`, `en-US`)
  - OCR Tier 2: GLM-OCR 0.9B on Ollama
  - Visual Embeddings: SigLIP 2 ONNX Runtime

---

## 3. Milestones Overview

- **M0**: Environment & Repository Bootstrap (Gate G0)
- **M1**: Arabic Text Core (Normalizer, Encodings, Stemmer, Chunker)
- **M2**: Arabic Corpus (>=200 files) & Eval Harness
- **M3**: Ollama Gateway & Model Bake-offs (Gate G3)
- **M4**: Deterministic Arabic Finder (Scanner, FTS5, Worker, RTL WPF UI)
- **M5**: Office & PDF Extractors (PdfPig/PDFium triage, quality gate)
- **M5b**: Legacy Formats (DOC, XLS, PPT)
- **M6**: Embeddings & Hybrid Search (USearch HNSW, RRF fusion, bidi UI)
- **M7**: Visual Pipeline (Windows OCR -> GLM-OCR, SigLIP 2 ONNX, Slow Lane)
- **M8**: Local Agent & Incremental Indexing (Call A/B, Tools, Grounding Validator)
- **M9**: Hardening, Performance Tuning & Offline Release (Gate G7)
