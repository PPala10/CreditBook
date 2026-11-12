using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CreditBook.Core;
using CreditBook.Storage;

namespace CreditBook.UI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly Func<Ledger> _createLedger;
    private Ledger? _ledger;
    private bool _loadingSelection;
    private List<CustomerSummary> _allCustomers = new();
    public ObservableCollection<CustomerSummary> Customers { get; } = new();
    public ObservableCollection<CustomerSummary> Relatives { get; } = new();
    public ObservableCollection<CustomerSummary> AvailableRelativeOptions { get; } = new();
    public ObservableCollection<DebtItemViewModel> Debts { get; } = new();
    public ObservableCollection<PaymentItemViewModel> Payments { get; } = new();
    public ObservableCollection<string> Notifications { get; } = new();

    [ObservableProperty] private CustomerSummary? selectedCustomer;
    [ObservableProperty] private CustomerSummary? selectedRelativeToAdd;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string newCustomerName = "";
    [ObservableProperty] private string newCustomerPhone = "";
    [ObservableProperty] private string newDebtAmount = "";
    [ObservableProperty] private string newDebtDescription = "";
    [ObservableProperty] private DateTimeOffset newDebtDate = DateTimeOffset.Now;
    [ObservableProperty] private string newPaymentAmount = "";
    [ObservableProperty] private decimal totalDebtOfSelectedCustomer;
    [ObservableProperty] private decimal familyTotalDebt;
    [ObservableProperty] private string errorMessage = "";
    [ObservableProperty] private string infoMessage = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanEdit))] private bool isBusy;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanEdit))] private bool isReady;
    [ObservableProperty] private bool isNotificationPanelOpen;
    public bool CanEdit => !IsBusy && IsReady;
    public bool SelectedCustomerHasLowScore => SelectedCustomer is { Score: < 50 };
    public int NotificationCount => Notifications.Count;

    public MainWindowViewModel(Ledger ledger) : this(() => ledger) { }
    public MainWindowViewModel(Func<Ledger> createLedger) => _createLedger = createLedger;
    public Task InitializeAsync() => RefreshAsync();

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedCustomerChanged(CustomerSummary? value)
    {
        if (!_loadingSelection && !IsBusy)
        {
            ApplyAccount(null);
            _ = RefreshAsync();
        }
    }

    private void ApplyFilter()
    {
        _loadingSelection = true;
        try
        {
            var id = SelectedCustomer?.Id;
            Customers.Clear();
            var text = SearchText.Trim();
            foreach (var customer in _allCustomers.Where(c => text.Length == 0
                || c.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)
                || (c.Phone?.Contains(text) ?? false))) Customers.Add(customer);
            SelectedCustomer = Customers.FirstOrDefault(c => c.Id == id);
            if (SelectedCustomer is null) ApplyAccount(null);
        }
        finally { _loadingSelection = false; }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = "";
        try { await LoadAsync(SelectedCustomer?.Id); IsReady = true; }
        catch (Exception ex)
        {
            IsReady = false;
            ApplyAccount(null);
            ErrorMessage = "Kayıtlar yüklenemedi. Yenile ile tekrar deneyin: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    private async Task LoadAsync(int? preferredId)
    {
        var state = await Task.Run(() =>
        {
            _ledger ??= _createLedger();
            var customers = _ledger.GetCustomers();
            var id = customers.FirstOrDefault(c => c.Id == preferredId)?.Id;
            if (!id.HasValue && preferredId is null) id = customers.FirstOrDefault()?.Id;
            return (customers, account: id.HasValue ? _ledger.GetAccount(id.Value) : null, overdue: _ledger.GetOverdue());
        });
        _allCustomers = state.customers.ToList();
        _loadingSelection = true;
        try
        {
            ApplyFilter();
            SelectedCustomer = Customers.FirstOrDefault(c => c.Id == state.account?.Customer.Id);
            ApplyAccount(SelectedCustomer is null ? null : state.account);
        }
        finally { _loadingSelection = false; }
        Notifications.Clear();
        foreach (var n in state.overdue)
            Notifications.Add($"{n.CustomerName}: {n.Outstanding:N2} ₺ kalan borç, 30 günü geçti ({n.Date:dd.MM.yyyy}).");
        OnPropertyChanged(nameof(NotificationCount));
    }

    private void ApplyAccount(AccountSummary? account)
    {
        Debts.Clear(); Payments.Clear(); Relatives.Clear(); AvailableRelativeOptions.Clear();
        TotalDebtOfSelectedCustomer = account?.Customer.Balance ?? 0;
        FamilyTotalDebt = account?.FamilyBalance ?? 0;
        if (account is not null)
        {
            foreach (var d in account.Debts) Debts.Add(new DebtItemViewModel(d));
            foreach (var p in account.Payments) Payments.Add(new PaymentItemViewModel(p));
            foreach (var r in account.Relatives) Relatives.Add(r);
            foreach (var c in _allCustomers.Where(c => c.Id != account.Customer.Id && !account.Relatives.Any(r => r.Id == c.Id)))
                AvailableRelativeOptions.Add(c);
        }
        SelectedRelativeToAdd = null;
        OnPropertyChanged(nameof(SelectedCustomerHasLowScore));
    }

    private async Task MutateAsync(Action<Ledger> action, Action? onSuccess = null)
    {
        if (!CanEdit) return;
        IsBusy = true; ErrorMessage = ""; InfoMessage = "";
        var selectedId = SelectedCustomer?.Id;
        try
        {
            await Task.Run(() => action(_ledger!));
            onSuccess?.Invoke();
            await LoadAsync(selectedId);
        }
        catch (ArgumentException ex) { ErrorMessage = ex.Message; }
        catch (Exception ex)
        {
            IsReady = false;
            ApplyAccount(null);
            ErrorMessage = "İşlem durumu doğrulanamadı. Yeniden işlem girmeden önce Yenile ile kayıtları kontrol edin: " + ex.Message;
        }
        finally { IsBusy = false; }
    }

    private static decimal ParseAmount(string text)
    {
        if (!decimal.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.GetCultureInfo("tr-TR"), out var amount))
            throw new ArgumentException("Geçerli bir tutar girin; ondalık için virgül kullanın (ör. 125,50).");
        LedgerRules.ValidateAmount(amount);
        return amount;
    }

    [RelayCommand] private Task AddCustomerAsync()
    {
        var name = NewCustomerName; var phone = NewCustomerPhone;
        return MutateAsync(l => l.AddCustomer(name, phone), () => { NewCustomerName = ""; NewCustomerPhone = ""; });
    }
    [RelayCommand] private Task ArchiveCustomerAsync(CustomerSummary? customer) => customer is null
        ? Task.CompletedTask : MutateAsync(l => l.ArchiveCustomer(customer.Id));
    [RelayCommand] private Task AddRelativeAsync()
    {
        var customer = SelectedCustomer; var relative = SelectedRelativeToAdd;
        return customer is null || relative is null ? Task.CompletedTask : MutateAsync(l => l.LinkRelative(customer.Id, relative.Id));
    }
    [RelayCommand] private Task RemoveRelativeAsync(CustomerSummary? relative)
    {
        var customer = SelectedCustomer;
        return customer is null || relative is null ? Task.CompletedTask : MutateAsync(l => l.UnlinkRelative(customer.Id, relative.Id));
    }
    [RelayCommand] private Task AddDebtAsync()
    {
        var customer = SelectedCustomer; var amount = NewDebtAmount; var description = NewDebtDescription; var date = NewDebtDate.Date;
        return customer is null ? Task.CompletedTask : MutateAsync(l => l.AddDebt(customer.Id, ParseAmount(amount), description, date),
            () => { NewDebtAmount = ""; NewDebtDescription = ""; NewDebtDate = DateTimeOffset.Now; });
    }
    [RelayCommand] private Task RecordPaymentAsync()
    {
        var customer = SelectedCustomer; var amount = NewPaymentAmount;
        return customer is null ? Task.CompletedTask : MutateAsync(l => l.RecordPayment(customer.Id, ParseAmount(amount), DateTime.Now), () => NewPaymentAmount = "");
    }
    [RelayCommand] private Task SettleDebtAsync(DebtItemViewModel? item) => item is null ? Task.CompletedTask : MutateAsync(l => l.SettleDebt(item.Id));
    [RelayCommand] private Task VoidDebtAsync(DebtItemViewModel? item) => item is null ? Task.CompletedTask : MutateAsync(l => l.VoidDebt(item.Id));
    [RelayCommand] private Task ReversePaymentAsync(PaymentItemViewModel? item) => item is null ? Task.CompletedTask : MutateAsync(l => l.ReversePayment(item.Id));
    [RelayCommand] private void ToggleNotificationPanel() => IsNotificationPanelOpen = !IsNotificationPanelOpen;
    public Task BackupAsync(string destination) => MutateAsync(l => l.Backup(destination), () => InfoMessage = "Yedek alındı: " + destination);
}
