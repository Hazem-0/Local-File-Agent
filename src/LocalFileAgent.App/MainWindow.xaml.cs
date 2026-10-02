using System.Windows;
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
            Title = "اختر مجلداً لفهرسة مستنداته وملفاته"
        };

        if (dialog.ShowDialog(this) == true && DataContext is SearchViewModel vm)
        {
            _ = vm.StartIndexingCommand.ExecuteAsync(dialog.FolderName);
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
        if (sender is FrameworkElement fe && fe.DataContext is SearchResultViewModel item)
        {
            item.OpenFileCommand.Execute(null);
        }
    }
}
