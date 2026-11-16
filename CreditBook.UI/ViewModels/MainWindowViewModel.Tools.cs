using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CreditBook.Core;
using CreditBook.Storage;

namespace CreditBook.UI.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty] private string editCustomerName = "";
    [ObservableProperty] private string editCustomerPhone = "";
    [ObservableProperty] private string editCustomerNote = "";
    [ObservableProperty] private string customerSort = "Ad";
    [ObservableProperty] private string debtFilter = "Tümü";
    [ObservableProperty] private string debtSort = "Tarih (yeniden)";
    [ObservableProperty] private DateTime? filterFrom;
    [ObservableProperty] private DateTime? filterTo;
    [ObservableProperty] private DateTimeOffset statementFrom = new(DateTime.Today.AddDays(1 - DateTime.Today.Day));
    [ObservableProperty] private DateTimeOffset statementTo = new(DateTime.Today);
    [ObservableProperty] private DateTimeOffset newDebtDueDate = DateTimeOffset.Now.AddDays(30);
    public ObservableCollection<string> DueDateHistory { get; } = new();
    public string[] CustomerSortOptions { get; } = ["Ad", "Bakiye (yüksekten)", "Bakiye (düşükten)"];
    public string[] DebtFilterOptions { get; } = ["Tümü", "Açık", "Kapalı", "Gecikmiş", "İptal"];
    public string[] DebtSortOptions { get; } = ["Tarih (yeniden)", "Tarih (eskiden)", "Vade (yakından)", "Tutar (yüksekten)", "Tutar (düşükten)"];
    private CustomerSummary? _editorSnapshot;

    partial void OnCustomerSortChanged(string value) => ApplyFilter();
    partial void OnDebtFilterChanged(string value) => ApplyDebtFilter();
    partial void OnDebtSortChanged(string value) => ApplyDebtFilter();
    partial void OnFilterFromChanged(DateTime? value) => ApplyDebtFilter();
    partial void OnFilterToChanged(DateTime? value) => ApplyDebtFilter();
    partial void OnNewDebtDateChanged(DateTimeOffset value) => NewDebtDueDate = value.AddDays(30);

    private void ApplyCustomerEditor(CustomerSummary? customer)
    {
        // A periodic refresh must not overwrite an unsaved customer edit.
        if (_editorSnapshot is not null && _editorSnapshot.Id == customer?.Id && customer is not null &&
            (EditCustomerName != _editorSnapshot.Name || EditCustomerPhone != (_editorSnapshot.Phone ?? "") || EditCustomerNote != (_editorSnapshot.Note ?? ""))) return;
        _editorSnapshot = customer;
        EditCustomerName = customer?.Name ?? "";
        EditCustomerPhone = customer?.Phone ?? "";
        EditCustomerNote = customer?.Note ?? "";
    }

    [RelayCommand] private Task SaveCustomerAsync()
    {
        var customer = SelectedCustomer;var name = EditCustomerName;var phone = EditCustomerPhone;var note = EditCustomerNote;
        return customer is null ? Task.CompletedTask : MutateAsync(l => l.UpdateCustomer(customer.Id, name, phone, note),
            () => { _editorSnapshot = null; InfoMessage = "Müşteri bilgileri güncellendi."; });
    }

    private void ApplyDebtFilter()
    {
        var rows = _allDebts.Where(d => DebtFilter switch {
            "Açık" => d.Outstanding > 0, "Kapalı" => d.Status == "Kapandı", "Gecikmiş" => d.IsOverdue, "İptal" => d.Status == "İptal", _ => true
        }).Where(d => (!FilterFrom.HasValue || d.Date.Date >= FilterFrom.Value.Date) && (!FilterTo.HasValue || d.Date.Date <= FilterTo.Value.Date));
        rows = DebtSort switch {
            "Tarih (eskiden)" => rows.OrderBy(d => d.Date).ThenBy(d => d.Id),
            "Vade (yakından)" => rows.OrderBy(d => d.DueDate).ThenBy(d => d.Id),
            "Tutar (yüksekten)" => rows.OrderByDescending(d => d.Amount).ThenBy(d => d.Id),
            "Tutar (düşükten)" => rows.OrderBy(d => d.Amount).ThenBy(d => d.Id),
            _ => rows.OrderByDescending(d => d.Date).ThenByDescending(d => d.Id)
        };
        Debts.Clear();foreach(var debt in rows) Debts.Add(debt);
    }

    [RelayCommand] private void ClearDebtFilters() { FilterFrom = null;FilterTo = null;DebtFilter = "Tümü"; }
    public Task ChangeDueDateAsync(int debtId, DateTime due, string reason) => MutateAsync(l => l.ChangeDueDate(debtId,due,reason));
    public Task VoidWithReasonAsync(int debtId, string reason) => MutateAsync(l => l.VoidDebt(debtId, reason));
    public Task ReverseWithReasonAsync(int paymentId, string reason) => MutateAsync(l => l.ReversePayment(paymentId, reason));

    public async Task ExportAsync(string destination, bool pdf)
    {
        if (!CanEdit || SelectedCustomer is null) return;
        var id=SelectedCustomer.Id;var from=StatementFrom.Date;var to=StatementTo.Date;
        IsBusy=true;ErrorMessage="";InfoMessage="";
        try
        {
            await Task.Run(() => {
                var statement=_ledger!.GetStatement(id,from,to);
                // Save picker owns overwrite confirmation; avoid partially replacing a previous export.
                var bytes=pdf ? StatementExport.Pdf(statement) : System.Text.Encoding.UTF8.GetBytes(StatementExport.Csv(statement));
                var temp=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
                try { File.WriteAllBytes(temp,bytes);File.Move(temp,destination,true); } finally {if(File.Exists(temp))File.Delete(temp);}
            });
            InfoMessage="Ekstre kaydedildi: "+destination;
        }
        catch(Exception ex){ErrorMessage="Ekstre oluşturulamadı: "+ex.Message;}
        finally{IsBusy=false;}
    }

    public Task RestoreAsync(string source) => MutateAsync(l => DatabaseRecovery.Restore(l.DatabasePath,source),
        () => InfoMessage="Yedek geri yüklendi. Önceki defterin güvenlik kopyası korundu.");
}
