using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Drawing.Wordprocessing;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using LocalFileAgent.Domain.Text;
using LocalFileAgent.Domain.Worker;
using LocalFileAgent.Text;
using WordText = DocumentFormat.OpenXml.Wordprocessing.Text;

namespace LocalFileAgent.Worker.Extractors;

public sealed class DocxExtractor
{
    private readonly IArabicOrderFixer _orderFixer;

    public DocxExtractor(IArabicOrderFixer? orderFixer = null)
    {
        _orderFixer = orderFixer ?? new ArabicOrderFixer();
    }

    public IReadOnlyList<ExtractedPage> Extract(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var doc = WordprocessingDocument.Open(stream, false);

        var pages = new List<ExtractedPage>();
        var currentPageSb = new StringBuilder();
        var currentPageNumber = 1;

        if (doc.MainDocumentPart?.Document.Body is { } body)
        {
            foreach (var element in body.ChildElements)
            {
                if (element is Paragraph p)
                {
                    // Check for hard page break inside paragraph runs
                    if (p.Descendants<Break>().Any(b => b.Type != null && b.Type.Value == BreakValues.Page))
                    {
                        FlushCurrentPage(pages, currentPageSb, currentPageNumber);
                        currentPageNumber++;
                    }

                    var pText = ExtractParagraphText(p);
                    if (!string.IsNullOrWhiteSpace(pText))
                    {
                        currentPageSb.AppendLine(pText);
                    }
                }
                else if (element is Table tbl)
                {
                    var tblText = ExtractTableText(tbl);
                    if (!string.IsNullOrWhiteSpace(tblText))
                    {
                        currentPageSb.AppendLine(tblText);
                    }
                }
            }
        }

        // Also check headers
        if (doc.MainDocumentPart?.HeaderParts != null)
        {
            foreach (var headerPart in doc.MainDocumentPart.HeaderParts)
            {
                var headerText = headerPart.Header?.InnerText;
                if (!string.IsNullOrWhiteSpace(headerText))
                {
                    currentPageSb.AppendLine(CultureInfo.InvariantCulture, $"[ترويسة]: {headerText}");
                }
            }
        }

        // Also check footers
        if (doc.MainDocumentPart?.FooterParts != null)
        {
            foreach (var footerPart in doc.MainDocumentPart.FooterParts)
            {
                var footerText = footerPart.Footer?.InnerText;
                if (!string.IsNullOrWhiteSpace(footerText))
                {
                    currentPageSb.AppendLine(CultureInfo.InvariantCulture, $"[تذييل]: {footerText}");
                }
            }
        }

        FlushCurrentPage(pages, currentPageSb, currentPageNumber);

        if (pages.Count == 0)
        {
            pages.Add(new ExtractedPage(1, string.Empty, "text_layer", 1.0f));
        }

        return pages;
    }

    private void FlushCurrentPage(List<ExtractedPage> pages, StringBuilder sb, int pageNumber)
    {
        var raw = sb.ToString().Trim();
        sb.Clear();

        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        // Check if text has reversed Arabic order
        var order = _orderFixer.Analyze(raw);
        var finalText = order.IsReversed ? _orderFixer.Fix(raw, order) : raw;

        pages.Add(new ExtractedPage(pageNumber, finalText, "text_layer", 1.0f));
    }

    private static string ExtractParagraphText(Paragraph p)
    {
        var sb = new StringBuilder();

        foreach (var run in p.Elements<Run>())
        {
            foreach (var text in run.Elements<WordText>())
            {
                sb.Append(text.Text);
            }
        }

        // Check for drawings with alt-text / descriptions
        foreach (var docPr in p.Descendants<DocProperties>())
        {
            if (!string.IsNullOrWhiteSpace(docPr.Description))
            {
                sb.Append(CultureInfo.InvariantCulture, $" [وصف الصورة: {docPr.Description}] ");
            }
            else if (!string.IsNullOrWhiteSpace(docPr.Title))
            {
                sb.Append(CultureInfo.InvariantCulture, $" [عنوان الصورة: {docPr.Title}] ");
            }
        }

        return sb.ToString().Trim();
    }

    private static string ExtractTableText(Table table)
    {
        var sb = new StringBuilder();

        foreach (var row in table.Elements<TableRow>())
        {
            var cellTexts = new List<string>();
            foreach (var cell in row.Elements<TableCell>())
            {
                var cellContent = cell.InnerText.Trim();
                cellTexts.Add(cellContent);
            }

            if (cellTexts.Count > 0)
            {
                sb.AppendLine("| " + string.Join(" | ", cellTexts) + " |");
            }
        }

        return sb.ToString().Trim();
    }
}
