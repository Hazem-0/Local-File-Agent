# Arabic Acceptance Evaluation Report (Milestone M2 Baseline)

**Date:** 2026-10-02 05:26:52 UTC  
**Corpus Size:** 228 synthetic files  
**Query Suite:** 63 benchmark queries  

## 1. OCR Accuracy Metrics

| Metric | Measured Baseline | Target Clean | Target Noisy |
|---|---|---|---|
| Raw CER | 9.48% | <= 25.0% | <= 45.0% |
| **Normalized CER** | **4.31%** | **<= 20.0%** | **<= 35.0%** |
| Raw WER | 11.38% | <= 30.0% | <= 50.0% |
| **Normalized WER** | **4.74%** | **<= 25.0%** | **<= 40.0%** |
| **Hallucination Rate** | **0.00%** | **<= 1.0%** | **<= 1.0%** |

## 2. Retrieval Accuracy Metrics

| Metric | Measured Score | Proposed Release Target |
|---|---|---|
| **Recall@1** | 12.70% | >= 65.0% |
| **Recall@5** | 41.27% | >= 80.0% |
| **Recall@10** | **61.90%** | **>= 85.0%** |
| **MRR** | **0.257** | **>= 0.750** |
| **nDCG@10** | **0.341** | **>= 0.800** |
| **Path-Scope Violations** | **0** | **0 (Hard Invariant)** |

## 3. Ground Truth Coverage by Category

- Text Documents (UTF-8, UTF-8 BOM, CP1256, UTF-16): 150 files
- Spreadsheets (CSV in UTF-8 and CP1256): 15 files
- Word Documents (.docx via OpenXML): 25 files
- Broken Text-Layer Fixtures (Reversed / Isolated): 15 files
- Scanned Document Images (PNG / JPG): 20 files
- Security Injection Test Fixtures: 3 files
- Total Corpus: 228 files
