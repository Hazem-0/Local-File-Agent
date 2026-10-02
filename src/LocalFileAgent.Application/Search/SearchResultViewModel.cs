using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalFileAgent.Application.FileSystem;
using LocalFileAgent.Domain.Search;
using LocalFileAgent.Domain.Text;
using LocalFileAgent.Text;

namespace LocalFileAgent.Application.Search;

public sealed partial class SearchResultViewModel : ObservableObject
{
    private readonly IFileLauncher _fileLauncher;

    public string FilePath { get; }
    public string DisplayPath { get; }
    public string FileName { get; }
    public string DisplayFileName { get; }
    public int PageNumber { get; }
    public string PageDisplay => PageNumber > 0 ? $"صفحة {PageNumber}" : string.Empty;
    public string SourceKind { get; }
    public string SourceKindDisplay { get; }
    public string MatchKind { get; }
    public string MatchKindDisplay => MatchKind switch
    {
        "hybrid" => "هجين (نصي + دلالي)",
        "vector" => "دلالي (معنى)",
        "fts" => "نصي (مطابقة)",
        _ => MatchKind
    };
    public float Score { get; }
    public string FormattedScore => Score.ToString("F2", CultureInfo.InvariantCulture);
    public string ScorePercentDisplay => $"{(int)Math.Clamp(Math.Round(Score * 100), 1, 100)}% صلة";
    public string RawSnippet { get; }
    public bool HasSnippet => !string.IsNullOrWhiteSpace(RawSnippet);
    public IReadOnlyList<SnippetRun> SnippetRuns { get; }

    public string Extension { get; }
    public string FileTypeIcon { get; }
    public string FileTypeBadgeBg { get; }
    public string FileTypeBadgeFg { get; }

    public string SourceBadgeBg => SourceKind switch
    {
        "ocr_win" or "ocr_glm" => "#FEF3C7", // Soft warm amber
        "vlm" => "#EDE9FE",                  // Soft violet
        "text_layer" => "#E0F2FE",           // Soft sky blue
        _ => "#F1F5F9"
    };

    public string SourceBadgeFg => SourceKind switch
    {
        "ocr_win" or "ocr_glm" => "#B45309",
        "vlm" => "#6D28D9",
        "text_layer" => "#0369A1",
        _ => "#475569"
    };

    public SearchResultViewModel(
        SearchResultItem item,
        string query,
        ITextNormalizer normalizer,
        IFileLauncher? fileLauncher = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(normalizer);

        _fileLauncher = fileLauncher ?? SafeFileLauncher.Instance;

        FilePath = item.FilePath;
        DisplayPath = BidiHelper.WrapLtrIsolate(item.FilePath);
        FileName = item.FileName;
        DisplayFileName = BidiHelper.WrapLtrIsolate(item.FileName);
        PageNumber = item.PageNumber;
        SourceKind = item.SourceKind;
        SourceKindDisplay = FormatSourceKind(item.SourceKind);
        MatchKind = item.MatchKind;
        Score = item.Score;

        var ext = Path.GetExtension(item.FilePath)?.TrimStart('.').ToUpperInvariant() ?? string.Empty;
        Extension = string.IsNullOrWhiteSpace(ext) ? "FILE" : ext;

        (FileTypeIcon, FileTypeBadgeBg, FileTypeBadgeFg) = GetFileTypeStyles(Extension);

        var cleanedSnippet = CleanSnippetMarkdown(item.Snippet);
        RawSnippet = cleanedSnippet;
        SnippetRuns = SnippetHighlighter.Highlight(cleanedSnippet, query, normalizer);
    }

    [RelayCommand]
    public void OpenFile()
    {
        _fileLauncher.OpenFile(FilePath);
    }

    [RelayCommand]
    public void OpenContainingFolder()
    {
        _fileLauncher.OpenContainingFolder(FilePath);
    }

    private static (string Icon, string Bg, string Fg) GetFileTypeStyles(string ext) => ext switch
    {
        "PDF" => ("📕", "#FEE2E2", "#DC2626"),
        "DOC" or "DOCX" => ("📘", "#DBEAFE", "#2563EB"),
        "XLS" or "XLSX" or "CSV" => ("📊", "#D1FAE5", "#059669"),
        "PPT" or "PPTX" => ("📙", "#FFEDD5", "#EA580C"),
        "PNG" or "JPG" or "JPEG" or "WEBP" or "BMP" or "TIFF" or "GIF" => ("🖼️", "#F3E8FF", "#9333EA"),
        "TXT" or "MD" or "JSON" or "XML" or "HTML" or "LOG" => ("📄", "#F1F5F9", "#475569"),
        _ => ("📁", "#F1F5F9", "#475569")
    };

    private static string FormatSourceKind(string sourceKind) => sourceKind switch
    {
        "text_layer" => "طبقة النص",
        "ocr_win" => "تعرف ضوئي OCR",
        "ocr_glm" => "تعرف ضوئي متقدم GLM",
        "vlm" => "وصف بصري",
        "meta" => "بيانات وصفية",
        _ => sourceKind
    };

    private static string CleanSnippetMarkdown(string? snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet)) return string.Empty;
        var text = snippet
            .Replace("**وصف محتوى الصورة:**", "وصف المحتوى:")
            .Replace("**نوع المستند:**", "نوع المستند:")
            .Replace("**العناصر البصرية والنصوص الرئيسية:**", "العناصر الرئيسية:")
            .Replace("**", string.Empty)
            .Replace("###", string.Empty)
            .Replace("##", string.Empty)
            .Replace("#", string.Empty)
            .Trim();

        while (text.Contains("\n\n\n", StringComparison.Ordinal))
        {
            text = text.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
        }
        return text;
    }
}
