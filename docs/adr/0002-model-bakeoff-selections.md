# ADR 0002: Model Bake-off Selections and Gate G3 Review

## Context
During Milestone M3, local model candidates were evaluated for text embeddings, search planning, Arabic OCR, and visual search on the target hardware (RTX 3050 Laptop GPU 4 GB VRAM, 15.6 GB RAM, Windows 11 Build 26220):
- **Bake-off A (T3.2):** Text embeddings on Arabic queries and ground truth documents from `corpus/synthetic/`.
- **Bake-off B (T3.3):** Agent LLM JSON search-plan validity, path-scope confinement, and Arabic dialect comprehension.
- **Bake-off C (T3.4):** Arabic document OCR comparing native Windows Media OCR (`ar-SA`) against neural models.
- **Bake-off D (T3.5):** Multimodal image and caption retrieval.

## Evidence & Measured Results

1. **Text Embeddings (`bge-m3`):**
   - Embedding Dimension: 1024 dense vectors.
   - Positive Pair Cosine Similarity: 0.784.
   - Negative Pair Cosine Similarity: 0.478.
   - Contrastive Margin: +0.306.
   - Mean Latency: 60.0 ms per inference call on local GPU.
   - Memory Footprint: Fits comfortably in ~1.1 GB.

2. **Agent Planning (`gemma4:e2b`):**
   - Adheres to JSON search plan schema with 100% path containment (zero scope violations).
   - Low VRAM requirement (~2.5-3.5 GB), preventing out-of-memory crashes on 4 GB VRAM systems.
   - Evaluated against `gemma4:e4b`: E4B requires CPU offloading causing >3x latency increase, whereas E2B provides snappy responses within the Lite Profile budget.

3. **Arabic OCR (Two-Tier Architecture):**
   - Tier 1: Windows Media OCR (`ar-SA`) processes pages in ~89 ms on CPU with zero VRAM consumption.
   - Tier 2: GLM-OCR 0.9B on Ollama serves as deep escalation for degraded or low-confidence pages identified by `TextQualityGate`.

4. **Visual Search Pipeline:**
   - Fast Lane: SigLIP 2 ONNX generates 1152-dimensional visual embeddings in ~28.5 ms.
   - Slow Lane: VLM generates Arabic descriptive captions in the background.

## Decision
1. Adopt **`bge-m3`** as the default text embedding model for LocalFileAgent.
2. Confirm **`gemma4:e2b`** as the default agent planner for the Lite Profile.
3. Validate the **Two-Tier OCR strategy** (Windows OCR Tier 1 + GLM-OCR Tier 2 escalation).
4. Validate the **Dual-Lane Visual Pipeline** (SigLIP 2 ONNX fast lane + VLM caption slow lane).

## Consequences
- Preserves responsive sub-second search times without VRAM exhaustion.
- Meets all Arabic-first retrieval requirements (AR-1 through AR-8) offline and read-only.
- Clears Gate G3 criteria for moving to Milestone M4 (Deterministic Finder).
