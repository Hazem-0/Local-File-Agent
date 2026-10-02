using System;
using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalFileAgent.Domain.Search;
using LocalFileAgent.Domain.Text;
using LocalFileAgent.Text;

namespace LocalFileAgent.Application.Search;

public sealed partial class SearchResultViewModel : ObservableObject
{
    public string FilePath { get; }
    public string DisplayPath { get; }
    public string FileName { get; }
    public string DisplayFileName { get; }
    public int PageNumber { get; }
    public string PageDisplay => PageNumber > 0 ? $"صفحة {PageNumber}" : string.Empty;
    public string SourceKind { get; }
    public string SourceKindDisplay { get; }
    public float Score { get; }
    public string FormattedScore => Score.ToString("F2", CultureInfo.InvariantCulture);
    public string RawSnippet { get; }
    public bool HasSnippet => !string.IsNullOrWhiteSpace(RawSnippet);
    public IReadOnlyList<SnippetRun> SnippetRuns { get; }

    public SearchResultViewModel(SearchResultItem item, string query, ITextNormalizer normalizer)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(normalizer);

        FilePath = item.FilePath;
        DisplayPath = BidiHelper.WrapLtrIsolate(item.FilePath);
        FileName = item.FileName;
        DisplayFileName = BidiHelper.WrapLtrIsolate(item.FileName);
        PageNumber = item.PageNumber;
        SourceKind = item.SourceKind;
        SourceKindDisplay = FormatSourceKind(item.SourceKind);
        Score = item.Score;
        RawSnippet = item.Snippet;
        SnippetRuns = SnippetHighlighter.Highlight(item.Snippet, query, normalizer);
    }

    private static string FormatSourceKind(string sourceKind) => sourceKind switch
    {
        "text_layer" => "طبقة النص",
        "ocr_win" => "تعرف ضوئي OCR",
        "ocr_glm" => "تعرف ضوئي متقدم GLM",
        "vlm" => "وصف بصري",
        "meta" => "بيانات وصفية",
        _ => sourceKind
    };
}
