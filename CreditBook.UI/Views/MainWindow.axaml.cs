using Avalonia.Controls;
using Avalonia.Interactivity;
using CreditBook.UI.ViewModels;

namespace CreditBook.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnBackupClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await vm.BackupDatabaseAsync(this);
        }
    }
}