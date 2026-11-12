using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CreditBook.UI.ViewModels;

namespace CreditBook.UI.Views;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    public MainWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm) await vm.InitializeAsync();
            _refreshTimer.Start();
        };
        _refreshTimer.Tick += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm) await vm.RefreshAsync();
        };
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private async void OnBackupClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            { Title = "Yedek klasörünü seçin", AllowMultiple = false });
            if (folders.Count == 0) return;
            var folder = folders[0].TryGetLocalPath();
            if (folder is null) { vm.ErrorMessage = "Yerel bir yedek klasörü seçin."; return; }
            await vm.BackupAsync(Path.Combine(folder, $"creditbook_backup_{DateTime.Now:yyyyMMdd_HHmmss_fff}.db"));
        }
        catch (Exception ex) { vm.ErrorMessage = "Yedekleme tamamlanamadı: " + ex.Message; }
    }
}
