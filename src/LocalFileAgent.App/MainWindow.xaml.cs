using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LocalFileAgent.Application.Search;
using Microsoft.Win32;

namespace LocalFileAgent.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnIndexFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "اختر مجلداً لفهرسة مستنداته وأرشفته محلياً"
        };

        if (dialog.ShowDialog(this) == true && DataContext is SearchViewModel vm)
        {
            vm.CurrentDirectoryPath = dialog.FolderName;
            _ = vm.StartIndexingCommand.ExecuteAsync(dialog.FolderName);
        }
    }

    private void OnUpdateArchiveClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is SearchViewModel vm)
        {
            if (string.IsNullOrWhiteSpace(vm.CurrentDirectoryPath) || !System.IO.Directory.Exists(vm.CurrentDirectoryPath))
            {
                OnIndexFolderClick(sender, e);
            }
            else
            {
                _ = vm.UpdateArchiveCommand.ExecuteAsync(null);
            }
        }
    }

    private void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is SearchViewModel vm)
        {
            _ = vm.SearchCommand.ExecuteAsync(null);
        }
    }

    private void OnResultItemMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement fe && fe.DataContext is SearchResultViewModel item)
        {
            item.OpenFileCommand.Execute(null);
        }
    }

    private void OnClearIndexClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is SearchViewModel vm)
        {
            var result = MessageBox.Show(
                this,
                "هل تريد بالتأكيد مسح كافة الوثائق المفهرسة من قاعدة بيانات التطبيق؟\n\n(ملاحظة: هذا الإجراء يلغي الفهرسة فقط، ولن يقوم بحذف أو تعديل أي ملفات أصلية على جهازك).",
                "تأكيد مسح الأرشيف",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            if (result == MessageBoxResult.Yes)
            {
                _ = vm.ClearIndexCommand.ExecuteAsync(null);
            }
        }
    }

    private void OnCopyAgentAnswerClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is SearchViewModel vm && !string.IsNullOrWhiteSpace(vm.AgentAnswerText))
        {
            Clipboard.SetText(vm.AgentAnswerText);
            MessageBox.Show(this, "تم نسخ إفادة الوكيل إلى الحافظة بنجاح.", "تم النسخ", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnCopyPathClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is SearchResultViewModel item)
        {
            Clipboard.SetText(item.FilePath);
        }
        else if (DataContext is SearchViewModel vm && vm.SelectedResult != null)
        {
            Clipboard.SetText(vm.SelectedResult.FilePath);
        }
    }

    private void OnQuickKeywordClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && DataContext is SearchViewModel vm)
        {
            var raw = btn.Content switch
            {
                string s => s,
                TextBlock tb => tb.Text,
                _ => btn.Content?.ToString() ?? string.Empty
            };
            var clean = raw.TrimStart('+', ' ').Trim();
            if (vm.Keywords.Any(k => string.Equals(k, clean, StringComparison.OrdinalIgnoreCase)))
            {
                vm.RemoveKeyword(clean);
            }
            else
            {
                vm.AddKeyword(clean);
            }
            AdditionalPhraseBox.Focus();
        }
    }

    private void OnAddPhraseClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is SearchViewModel vm && !string.IsNullOrWhiteSpace(AdditionalPhraseBox.Text))
        {
            vm.AddKeyword(AdditionalPhraseBox.Text);
            AdditionalPhraseBox.Text = string.Empty;
            AdditionalPhraseBox.Focus();
        }
    }

    private void OnRemoveKeywordCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string keyword && DataContext is SearchViewModel vm)
        {
            vm.RemoveKeyword(keyword);
            AdditionalPhraseBox.Focus();
        }
    }

    private void OnAdditionalPhraseKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnAddPhraseClick(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            AdditionalPhraseBox.Text = string.Empty;
            e.Handled = true;
        }
    }
}
