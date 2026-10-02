# ADR 0001: Architecture Decisions for LocalFileAgent v2 (Arabic-First)

## Context
LocalFileAgent is being developed as an offline, read-only, Arabic-first desktop search agent on Windows 11.
Environment inspection revealed:
- CPU: Intel Core i5-12450H (8 cores / 12 logical processors)
- RAM: 15.6 GB
- GPU: NVIDIA GeForce RTX 3050 Laptop GPU (4 GB VRAM)
- Windows OCR: `ar-SA` (Arabic) and `en-US` (English) recognizers are installed and verified available.
- Ollama: version 0.35.0 on loopback `127.0.0.1:11434`.

## Options Considered

### 1. Hardware & Model Profile
- **Option A (Standard Profile):** Target Gemma 4 E4B / Qwen3-VL 4B with partial CPU offloading.
- **Option B (Lite Profile - Chosen):** Target Gemma 4 E2B / Qwen3-VL 2B, fitting fully inside the 4 GB VRAM for maximum responsiveness, while keeping E4B as an optional quality upgrade during Phase 0 / Milestone M3 bake-offs.

### 2. Arabic OCR Strategy
- **Option A (Single Tier):** Use Windows OCR only or model-based OCR only.
- **Option B (Two-Tier with Gate - Chosen):** Tier 1 Windows OCR (ar-SA/en-US) as fast CPU baseline with zero VRAM overhead. Pages failing the text quality gate (Arabic character ratio, disconnected letter detector, printable ratio) escalate to Tier 2 (GLM-OCR 0.9B on Ollama) equipped with repetition and hallucination guards.

### 3. Vector & Retrieval Architecture
- **Option A (Pure Lexical / SQLite):** FTS5 only.
- **Option B (Hybrid Dual Vector Space - Chosen):** Dual vector space (Text vectors via BGE-M3 + Visual vectors via SigLIP 2 ONNX) fused with SQLite FTS5 (unicode61 + trigram) via weighted Reciprocal Rank Fusion (RRF). USearch HNSW with exact brute-force fallback for scopes <50,000 chunks to enforce zero scope violations.

### 4. UI Layout & Localization
- **Option A (English Primary):** English UI with Arabic search support.
- **Option B (Arabic Primary RTL - Chosen):** Native RTL layout (`ar-EG` primary, toggleable to English), with strict LTR Unicode isolates (U+2066..U+2069) for file paths and names to ensure bi-directional visual integrity.

## Decision
Adopt Option B for all four categories.

## Consequences
- Fast sub-second search times and responsive local agent inference within the 4 GB VRAM budget.
- Zero extra administrative installations required for Tier 1 Arabic OCR.
- Robust handling of scanned Arabic PDFs and corrupted text layers without UI freezes.
