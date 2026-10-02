using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UglyToad.PdfPig;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;
using LocalFileAgent.Domain.Text;
using LocalFileAgent.Domain.Worker;
using LocalFileAgent.Text;

namespace LocalFileAgent.Worker.Extractors;

public sealed class PdfExtractor
{
    private readonly ITextQualityGate _qualityGate;
    private readonly IArabicOrderFixer _orderFixer;
    private readonly OcrEngine? _ocrEngine;

    public PdfExtractor(
        ITextQualityGate? qualityGate = null,
        IArabicOrderFixer? orderFixer = null,
        OcrEngine? ocrEngine = null)
    {
        _qualityGate = qualityGate ?? new TextQualityGate();
        _orderFixer = orderFixer ?? new ArabicOrderFixer();
        _ocrEngine = ocrEngine ?? TryInitOcrEngine();
    }

    private static OcrEngine? TryInitOcrEngine()
    {
        try
        {
            return OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("ar-SA"))
                   ?? OcrEngine.TryCreateFromUserProfileLanguages();
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<ExtractedPage>> ExtractAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var extractedPages = new List<ExtractedPage>();

        using (var pdfPigDoc = UglyToad.PdfPig.PdfDocument.Open(filePath))
        {
            var pageCount = pdfPigDoc.NumberOfPages;

            for (var pageNum = 1; pageNum <= pageCount; pageNum++)
            {
                var page = pdfPigDoc.GetPage(pageNum);
                var digitalText = page.Text?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(digitalText))
                {
                    // Case 1: Page has no digital text (scanned or image-only) -> Route to OCR
                    var ocrResult = await TryOcrPageAsync(filePath, pageNum).ConfigureAwait(false);
                    if (ocrResult != null && !string.IsNullOrWhiteSpace(ocrResult.Value.Text))
                    {
                        var ocrText = ocrResult.Value.Text;
                        var ocrOrder = _orderFixer.Analyze(ocrText);
                        var fixedOcr = ocrOrder.IsReversed ? _orderFixer.Fix(ocrText, ocrOrder) : ocrText;
                        extractedPages.Add(new ExtractedPage(pageNum, fixedOcr, "ocr_win", ocrResult.Value.Confidence));
                    }
                    else
                    {
                        extractedPages.Add(new ExtractedPage(pageNum, string.Empty, "ocr_win", 0.0f));
                    }
                    continue;
                }

                // Case 2: Digital text layer is present -> Inspect quality
                var quality = _qualityGate.Score(digitalText, new LanguageHint("ar"));

                if (quality.Passed)
                {
                    // Digital text is valid. Check if logical order is reversed
                    var order = _orderFixer.Analyze(digitalText);
                    var finalText = order.IsReversed ? _orderFixer.Fix(digitalText, order) : digitalText;
                    extractedPages.Add(new ExtractedPage(pageNum, finalText, "text_layer", quality.Score));
                }
                else
                {
                    // Case 3: Digital text is corrupted / broken (e.g. cid glyphs, single letter runs) -> Escalate to OCR
                    var ocrResult = await TryOcrPageAsync(filePath, pageNum).ConfigureAwait(false);
                    if (ocrResult != null && !string.IsNullOrWhiteSpace(ocrResult.Value.Text))
                    {
                        var ocrText = ocrResult.Value.Text;
                        var ocrOrder = _orderFixer.Analyze(ocrText);
                        var fixedOcr = ocrOrder.IsReversed ? _orderFixer.Fix(ocrText, ocrOrder) : ocrText;
                        extractedPages.Add(new ExtractedPage(pageNum, fixedOcr, "ocr_win", ocrResult.Value.Confidence));
                    }
                    else
                    {
                        // Fallback to digital text if OCR could not render
                        extractedPages.Add(new ExtractedPage(pageNum, digitalText, "text_layer_degraded", quality.Score));
                    }
                }
            }
        }

        if (extractedPages.Count == 0)
        {
            extractedPages.Add(new ExtractedPage(1, string.Empty, "text_layer", 1.0f));
        }

        return extractedPages;
    }

    private async Task<(string Text, float Confidence)?> TryOcrPageAsync(string filePath, int pageNumber)
    {
        if (_ocrEngine == null || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(filePath));
            var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(storageFile);

            if (pageNumber < 1 || pageNumber > pdfDoc.PageCount)
            {
                return null;
            }

            using var page = pdfDoc.GetPage((uint)(pageNumber - 1));
            using var renderStream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(renderStream);

            renderStream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(renderStream);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

            var ocrResult = await _ocrEngine.RecognizeAsync(softwareBitmap);
            return (ocrResult.Text, 0.88f);
        }
        catch
        {
            return null;
        }
    }
}
