using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using LocalFileAgent.Domain.Worker;

namespace LocalFileAgent.Worker.Extractors;

public sealed class XlsxExtractor
{
    public static IReadOnlyList<ExtractedPage> Extract(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var spreadsheetDoc = SpreadsheetDocument.Open(stream, false);

        var pages = new List<ExtractedPage>();
        var workbookPart = spreadsheetDoc.WorkbookPart;
        if (workbookPart == null)
        {
            return [new ExtractedPage(1, string.Empty, "text_layer", 1.0f)];
        }

        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable.Elements<SharedStringItem>().ToList();
        var sheets = workbookPart.Workbook.Sheets?.Elements<Sheet>().ToList();
        if (sheets == null || sheets.Count == 0)
        {
            return [new ExtractedPage(1, string.Empty, "text_layer", 1.0f)];
        }

        for (var i = 0; i < sheets.Count; i++)
        {
            var sheet = sheets[i];
            var sheetName = sheet.Name?.Value ?? $"ورقة {i + 1}";
            var relId = sheet.Id?.Value;
            if (string.IsNullOrEmpty(relId))
            {
                continue;
            }

            var worksheetPart = workbookPart.GetPartById(relId) as WorksheetPart;
            if (worksheetPart == null)
            {
                continue;
            }

            var sheetSb = new StringBuilder();
            sheetSb.AppendLine(CultureInfo.InvariantCulture, $"[ورقة العمل: {sheetName}]");

            var rows = worksheetPart.Worksheet.Descendants<Row>();
            foreach (var row in rows)
            {
                var cellValues = new List<string>();
                foreach (var cell in row.Elements<Cell>())
                {
                    var val = GetCellValue(cell, sharedStrings);
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        cellValues.Add(val);
                    }
                }

                if (cellValues.Count > 0)
                {
                    sheetSb.AppendLine("| " + string.Join(" | ", cellValues) + " |");
                }
            }

            var raw = sheetSb.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                pages.Add(new ExtractedPage(i + 1, raw, "text_layer", 1.0f));
            }
        }

        if (pages.Count == 0)
        {
            pages.Add(new ExtractedPage(1, string.Empty, "text_layer", 1.0f));
        }

        return pages;
    }

    private static string GetCellValue(Cell cell, List<SharedStringItem>? sharedStrings)
    {
        var value = cell.CellValue?.Text;
        if (string.IsNullOrEmpty(value))
        {
            return cell.InnerText.Trim();
        }

        if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) &&
                sharedStrings != null && id >= 0 && id < sharedStrings.Count)
            {
                return sharedStrings[id].InnerText.Trim();
            }
        }

        return value.Trim();
    }
}
