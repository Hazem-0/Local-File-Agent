# Environment & Baseline Report

Generated during Milestone M0 on 2026-10-02.

## Operating System & Hardware

| Component | Value / Specification | Notes |
|---|---|---|
| OS Caption | Microsoft Windows 11 Home Single Language | 64-bit |
| OS Version / Build | 10.0.26220 (Build 26220) | Fully supports Windows Media OCR and DirectML |
| CPU | 12th Gen Intel(R) Core(TM) i5-12450H | 8 Cores (4P + 4E), 12 Logical Processors |
| Physical RAM | 15.6 GB | Suitable for Standard/Lite profile |
| Dedicated GPU | NVIDIA GeForce RTX 3050 Laptop GPU | 4096 MiB VRAM (Driver 591.44) |
| Integrated GPU | Intel(R) UHD Graphics | 1024 MiB shared |
| Drive C: | 39.1 GB Free / 204.1 GB Total | System drive |
| Drive D: | 137.2 GB Free / 72.5 GB Used | Working directory & model/index volume |

## Installed Tools & Runtimes

| Tool | Status / Version | Path / Notes |
|---|---|---|
| Git | 2.45+ | `C:\Program Files\Git\cmd\git.exe` |
| Winget | Available | `C:\Users\Hazem\AppData\Local\Microsoft\WindowsApps\winget.exe` |
| Ollama CLI & API | 0.35.0 (Running on 127.0.0.1:11434) | Loopback verified; 0 models currently installed |
| .NET Runtimes | 8.0.21, 8.0.28 (Desktop & Core) | `C:\Program Files\dotnet\shared` |
| .NET SDK | Installing .NET 10.0 SDK | Target runtime for solution is `net10.0` |
| Tesseract | Not installed | Optional Tier 1b OCR candidate |
| LibreOffice | Not installed | Optional converter for legacy `.doc`/`.xls` (M5b) |

## Windows OCR Capabilities

Query against `Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages`:
- `ar-SA` (Arabic - Saudi Arabia) — **Installed & Available**
- `en-US` (English - United States) — **Installed & Available**

*Conclusion:* Tier 1 Windows OCR for Arabic and English works completely offline out-of-the-box without requiring `Add-WindowsCapability` or administrator elevation.

## Recommended Hardware Profile

- **Profile Chosen:** **Lite Profile** (tailored to 4 GB VRAM)
- **Agent LLM + Vision:** `gemma4:e2b` (alt: `qwen3-vl:2b`) — fits fully within 4 GB VRAM for rapid response times.
- **Text Embeddings:** `bge-m3` via Ollama `/api/embed` (or `embeddinggemma`).
- **OCR Tier 1:** Windows OCR (`ar-SA`, `en-US`).
- **OCR Tier 2:** `glm-ocr:0.9b` on Ollama (escalation for pages failing quality gate).
- **Visual Embeddings:** SigLIP 2 via ONNX Runtime.
- **Evaluation Candidate at Gate G3:** `gemma4:e4b` for Quality profile comparison.

## Model Download Package List for Gate G1 Approval

| Role | Model Tag | Approximate Size | VRAM Footprint |
|---|---|---|---|
| Agent & Vision (Lite) | `gemma4:e2b` | ~2.5 GB | ~3.2 GB |
| Agent & Vision (Alt) | `qwen3-vl:2b` | ~1.9 GB | ~2.5 GB |
| Text Embeddings | `bge-m3` | ~1.2 GB | ~0.8 GB (or CPU) |
| OCR Tier 2 | `glm-ocr` | ~1.5 GB | ~1.5 GB |
| **Total Download (Initial)** | — | **~5.2 – 7.1 GB** | Within disk & memory budget |

*(Note: No models will be pulled until Gate G1 review and user authorization.)*
