using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using LocalFileAgent.Application.Search;

namespace LocalFileAgent.App;

public static class SnippetHighlightBehavior
{
    private static readonly Brush HighlightBackground = new SolidColorBrush(Color.FromArgb(90, 255, 215, 0)); // Soft gold highlight
    private static readonly Brush HighlightForeground = new SolidColorBrush(Color.FromRgb(180, 80, 0));

    static SnippetHighlightBehavior()
    {
        HighlightBackground.Freeze();
        HighlightForeground.Freeze();
    }

    public static readonly DependencyProperty RunsProperty =
        DependencyProperty.RegisterAttached(
            "Runs",
            typeof(IReadOnlyList<SnippetRun>),
            typeof(SnippetHighlightBehavior),
            new PropertyMetadata(null, OnRunsChanged));

    public static IReadOnlyList<SnippetRun>? GetRuns(DependencyObject element) =>
        (IReadOnlyList<SnippetRun>?)element.GetValue(RunsProperty);

    public static void SetRuns(DependencyObject element, IReadOnlyList<SnippetRun>? value) =>
        element.SetValue(RunsProperty, value);

    private static void OnRunsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock)
        {
            return;
        }

        textBlock.Inlines.Clear();

        if (e.NewValue is not IReadOnlyList<SnippetRun> runs || runs.Count == 0)
        {
            return;
        }

        foreach (var run in runs)
        {
            var inlineRun = new Run(run.Text);
            if (run.IsHighlighted)
            {
                inlineRun.Background = HighlightBackground;
                inlineRun.Foreground = HighlightForeground;
                inlineRun.FontWeight = FontWeights.Bold;
            }

            textBlock.Inlines.Add(inlineRun);
        }
    }
}
