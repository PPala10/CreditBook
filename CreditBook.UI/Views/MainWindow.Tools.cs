using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CreditBook.UI.ViewModels;

namespace CreditBook.UI.Views;

public partial class MainWindow
{
    private async void OnVoidDebtClick(object? sender,RoutedEventArgs e)
    {
        if(DataContext is not MainWindowViewModel vm || sender is not Control {DataContext:DebtItemViewModel debt})return;
        _refreshTimer.Stop();
        try {var result=await LedgerDialogs.ReasonAsync(this,$"Borç #{debt.Id} iptal gerekçesi");if(result.HasValue)await vm.VoidWithReasonAsync(debt.Id,result.Value.Reason);}
        finally{_refreshTimer.Start();}
    }
    private async void OnReversePaymentClick(object? sender,RoutedEventArgs e)
    {
        if(DataContext is not MainWindowViewModel vm || sender is not Control {DataContext:PaymentItemViewModel payment})return;
        _refreshTimer.Stop();
        try {var result=await LedgerDialogs.ReasonAsync(this,$"{payment.Amount:N2} ₺ tahsilatı geri alma gerekçesi");if(result.HasValue)await vm.ReverseWithReasonAsync(payment.Id,result.Value.Reason);}
        finally{_refreshTimer.Start();}
    }
    private async void OnChangeDueClick(object? sender,RoutedEventArgs e)
    {
        if(DataContext is not MainWindowViewModel vm || sender is not Control {DataContext:DebtItemViewModel debt})return;
        _refreshTimer.Stop();
        try {var result=await LedgerDialogs.ReasonAsync(this,$"Borç #{debt.Id} vadesini değiştir",debt.DueDate);if(result?.Due is {} due)await vm.ChangeDueDateAsync(debt.Id,due,result.Value.Reason);}
        finally{_refreshTimer.Start();}
    }
    private async void OnExportCsvClick(object? sender,RoutedEventArgs e)=>await SaveStatementAsync(false,false);
    private async void OnExportPdfClick(object? sender,RoutedEventArgs e)=>await SaveStatementAsync(true,false);
    private async void OnPrintClick(object? sender,RoutedEventArgs e)=>await SaveStatementAsync(true,true);
    private async Task SaveStatementAsync(bool pdf,bool open)
    {
        if(DataContext is not MainWindowViewModel vm || vm.SelectedCustomer is null)return;
        _refreshTimer.Stop();
        try {
            var extension=pdf?"pdf":"csv";
            var file=await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
                Title="Hesap ekstresini kaydet",SuggestedFileName=$"CreditBook-ekstre-{vm.SelectedCustomer.Id}-{DateTime.Now:yyyyMMdd}.{extension}",
                DefaultExtension=extension,ShowOverwritePrompt=true,FileTypeChoices=[new FilePickerFileType(extension.ToUpperInvariant()){Patterns=[$"*.{extension}"]}]
            });
            if(file?.TryGetLocalPath() is not {} path)return;
            await vm.ExportAsync(path,pdf);
            if(open&&string.IsNullOrEmpty(vm.ErrorMessage))Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
        }catch(Exception ex){vm.ErrorMessage="Ekstre işlemi tamamlanamadı: "+ex.Message;}
        finally{_refreshTimer.Start();}
    }
    private async void OnBackupSettingsClick(object? sender,RoutedEventArgs e)
    {
        if(DataContext is not MainWindowViewModel vm)return;
        _refreshTimer.Stop();
        try {await vm.PauseAutomaticBackupsAsync();await new BackupWindow(vm).ShowDialog(this);vm.UpdateBackupStatus();}
        catch(Exception ex){vm.ErrorMessage="Yedek ayarları açılamadı: "+ex.Message;}
        finally{vm.ResumeAutomaticBackups();_refreshTimer.Start();}
    }
}
