using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using LocalFileAgent.Domain.Text;
using LocalFileAgent.Domain.Worker;
using LocalFileAgent.Text;
using A = DocumentFormat.OpenXml.Drawing;

namespace LocalFileAgent.Worker.Extractors;

public sealed class PptxExtractor
{
    private readonly IArabicOrderFixer _orderFixer;

    public PptxExtractor(IArabicOrderFixer? orderFixer = null)
    {
        _orderFixer = orderFixer ?? new ArabicOrderFixer();
    }

    public IReadOnlyList<ExtractedPage> Extract(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var presentationDoc = PresentationDocument.Open(stream, false);

        var pages = new List<ExtractedPage>();
        var presentationPart = presentationDoc.PresentationPart;
        if (presentationPart?.Presentation.SlideIdList == null)
        {
            return [new ExtractedPage(1, string.Empty, "text_layer", 1.0f)];
        }

        var slideIds = presentationPart.Presentation.SlideIdList.Elements<SlideId>().ToList();
        for (var i = 0; i < slideIds.Count; i++)
        {
            var slideId = slideIds[i];
            var relId = slideId.RelationshipId?.Value;
            if (string.IsNullOrEmpty(relId))
            {
                continue;
            }

            var slidePart = presentationPart.GetPartById(relId) as SlidePart;
            if (slidePart == null)
            {
                continue;
            }

            var slideSb = new StringBuilder();

            // Extract all text elements on slide
            var textElements = slidePart.Slide.Descendants<A.Text>();
            foreach (var t in textElements)
            {
                if (!string.IsNullOrWhiteSpace(t.Text))
                {
                    slideSb.AppendLine(t.Text);
                }
            }

            // Extract shape alt text / descriptions
            var nonVisualProps = slidePart.Slide.Descendants<NonVisualDrawingProperties>();
            foreach (var p in nonVisualProps)
            {
                if (!string.IsNullOrWhiteSpace(p.Description))
                {
                    slideSb.AppendLine(CultureInfo.InvariantCulture, $"[وصف الشكل: {p.Description}]");
                }
            }

            // Extract slide notes if present
            if (slidePart.NotesSlidePart != null)
            {
                var noteTexts = slidePart.NotesSlidePart.NotesSlide.Descendants<A.Text>()
                    .Select(n => n.Text)
                    .Where(n => !string.IsNullOrWhiteSpace(n));

                var joinedNotes = string.Join(" ", noteTexts);
                if (!string.IsNullOrWhiteSpace(joinedNotes))
                {
                    slideSb.AppendLine(CultureInfo.InvariantCulture, $"[ملاحظات الشريحة]: {joinedNotes}");
                }
            }

            var raw = slideSb.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var order = _orderFixer.Analyze(raw);
                var finalText = order.IsReversed ? _orderFixer.Fix(raw, order) : raw;
                pages.Add(new ExtractedPage(i + 1, finalText, "text_layer", 1.0f));
            }
        }

        if (pages.Count == 0)
        {
            pages.Add(new ExtractedPage(1, string.Empty, "text_layer", 1.0f));
        }

        return pages;
    }
}
