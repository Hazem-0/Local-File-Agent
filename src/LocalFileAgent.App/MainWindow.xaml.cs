using System.Windows;
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
            Title = "اختر مجلداً لفهرسة ملفاته"
        };

        if (dialog.ShowDialog(this) == true && DataContext is SearchViewModel vm)
        {
            _ = vm.StartIndexingCommand.ExecuteAsync(dialog.FolderName);
        }
    }
}
