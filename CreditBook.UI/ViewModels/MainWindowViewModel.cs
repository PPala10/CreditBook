using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CreditBook.UI.Data;
using CreditBook.UI.Models;
using CreditBook.UI.Search;
using System;
using Avalonia.Threading;
using CreditBook.UI.Common;
using Microsoft.EntityFrameworkCore;
using System.IO;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System.Threading.Tasks;

namespace CreditBook.UI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly CreditBookDbContext _db = new();
    private readonly CustomerTrie _trie = new();
    private List<Customer> _allCustomers = new();

    public ObservableCollection<Customer> Customers { get; } = new();
    public ObservableCollection<DebtItemViewModel> Debts { get; } = new();
    public ObservableCollection<Customer> Relatives { get; } = new();
    public ObservableCollection<Customer> AvailableRelativeOptions { get; } = new();
    public ObservableCollection<string> Notifications { get; } = new();


    [ObservableProperty]
    private Customer? selectedCustomer;

    [ObservableProperty]
    private Customer? selectedRelativeToAdd;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string newCustomerName = string.Empty;

    [ObservableProperty]
    private string newCustomerPhone = string.Empty;

    [ObservableProperty]
    private string customerFormError = string.Empty;

    [ObservableProperty]
    private string newDebtAmount = string.Empty;

    [ObservableProperty]
    private string newDebtDescription = string.Empty;

    [ObservableProperty]
    private decimal totalDebtOfSelectedCustomer;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnreadNotifications))]
    private int unreadNotificationCount;

    public bool HasUnreadNotifications => UnreadNotificationCount > 0;

    [ObservableProperty]
    private bool isNotificationPanelOpen;
    
    private readonly DispatcherTimer _overdueCheckTimer;
    
    [ObservableProperty]
    private DateTimeOffset newDebtDate = DateTimeOffset.Now;
    
    public bool SelectedCustomerHasLowScore =>
        SelectedCustomer is not null && SelectedCustomer.Score < ScoringSettings.WarningThreshold;

    
    [ObservableProperty]
    private string backupStatusMessage = string.Empty;

    public MainWindowViewModel()
    {
        _db.Database.EnsureCreated();
        LoadCustomers();
        
        _overdueCheckTimer = new DispatcherTimer
        {
            Interval = NotificationSettings.CheckInterval
        };
        _overdueCheckTimer.Tick += (_, _) => CheckOverdueDebts();
        _overdueCheckTimer.Start();

        CheckOverdueDebts();
    }

    // ---------- Customer loading / search ----------

    private void LoadCustomers()
    {
        _allCustomers = _db.Customers.OrderBy(c => c.Name).ToList();
        _trie.Rebuild(_allCustomers);
        ApplyCustomerFilter();
    }

    private void ApplyCustomerFilter()
    {
        Customers.Clear();

        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allCustomers.OrderBy(c => c.Name)
            : _trie.SearchByPrefix(SearchText).OrderBy(c => c.Name);

        foreach (var customer in filtered)
        {
            Customers.Add(customer);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyCustomerFilter();
    }

    // ---------- Phone number restriction (11-digit numeric) ----------

    partial void OnNewCustomerPhoneChanged(string value)
    {
        var digitsOnly = Regex.Replace(value, "[^0-9]", "");

        if (digitsOnly.Length > 11)
        {
            digitsOnly = digitsOnly[..11];
        }

        if (digitsOnly != value)
        {
            NewCustomerPhone = digitsOnly;
        }
    }

    // ---------- Selected customer changes ----------

    partial void OnSelectedCustomerChanged(Customer? value)
    {
        LoadRelatives();
        LoadDebts();
        OnPropertyChanged(nameof(SelectedCustomerHasLowScore));
    }

    private void LoadRelatives()
    {
        Relatives.Clear();
        AvailableRelativeOptions.Clear();

        if (SelectedCustomer is null)
            return;

        var links = _db.CustomerRelatives
            .Where(r => r.CustomerId == SelectedCustomer.Id || r.RelativeId == SelectedCustomer.Id)
            .ToList();

        var relativeIds = links
            .Select(r => r.CustomerId == SelectedCustomer.Id ? r.RelativeId : r.CustomerId)
            .Distinct()
            .ToHashSet();

        foreach (var customer in _allCustomers.Where(c => relativeIds.Contains(c.Id)))
        {
            Relatives.Add(customer);
        }

        foreach (var customer in _allCustomers.Where(c => c.Id != SelectedCustomer.Id && !relativeIds.Contains(c.Id)))
        {
            AvailableRelativeOptions.Add(customer);
        }
    }

    // ---------- Debts (own + relatives combined) ----------

    private void LoadDebts()
    {
        Debts.Clear();

        if (SelectedCustomer is null)
        {
            TotalDebtOfSelectedCustomer = 0;
            return;
        }

        var relatedCustomerIds = Relatives.Select(r => r.Id).ToList();
        var allRelevantIds = new List<int> { SelectedCustomer.Id };
        allRelevantIds.AddRange(relatedCustomerIds);

        var nameById = new Dictionary<int, string> { [SelectedCustomer.Id] = SelectedCustomer.Name };
        foreach (var relative in Relatives)
        {
            nameById[relative.Id] = relative.Name;
        }

        var debts = _db.DebtRecords
            .Where(d => allRelevantIds.Contains(d.CustomerId))
            .OrderBy(d => d.Date)
            .ToList();

        foreach (var debt in debts)
        {
            var ownerName = nameById.TryGetValue(debt.CustomerId, out var name) ? name : "Unknown";
            Debts.Add(new DebtItemViewModel(debt, ownerName));
        }

        RecalculateTotal();
    }

    private void RecalculateTotal()
    {
        TotalDebtOfSelectedCustomer = Debts.Where(d => !d.IsPaid).Sum(d => d.Amount);
    }

    // ---------- Customer commands ----------

    [RelayCommand]
    private void AddCustomer()
    {
        CustomerFormError = string.Empty;

        if (string.IsNullOrWhiteSpace(NewCustomerName))
        {
            CustomerFormError = "İsim boş olamaz.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewCustomerPhone))
        {
            CustomerFormError = "Telefon numarası zorunludur.";
            return;
        }

        if (NewCustomerPhone.Length != 11)
        {
            CustomerFormError = "Telefon numarası 11 haneli olmalı.";
            return;
        }

        var customer = new Customer
        {
            Name = NewCustomerName.Trim(),
            Phone = NewCustomerPhone        
        };

        _db.Customers.Add(customer);
        _db.SaveChanges();

        _allCustomers.Add(customer);
        _trie.Rebuild(_allCustomers);
        ApplyCustomerFilter();

        NewCustomerName = string.Empty;
        NewCustomerPhone = string.Empty;
    }

    [RelayCommand]
    private void DeleteCustomer(Customer? customer)
    {
        if (customer is null)
            return;

        // Bu müşteriye ait akrabalık bağlarını da temizle
        var links = _db.CustomerRelatives
            .Where(r => r.CustomerId == customer.Id || r.RelativeId == customer.Id)
            .ToList();
        _db.CustomerRelatives.RemoveRange(links);

        _db.Customers.Remove(customer);
        _db.SaveChanges();

        _allCustomers.Remove(customer);
        _trie.Rebuild(_allCustomers);
        ApplyCustomerFilter();

        if (SelectedCustomer == customer)
        {
            SelectedCustomer = null;
        }
    }

    // ---------- Relative commands ----------

    [RelayCommand]
    private void AddRelative()
    {
        if (SelectedCustomer is null || SelectedRelativeToAdd is null)
            return;

        if (SelectedRelativeToAdd.Id == SelectedCustomer.Id)
            return;

        var link = new CustomerRelative
        {
            CustomerId = SelectedCustomer.Id,
            RelativeId = SelectedRelativeToAdd.Id
        };

        _db.CustomerRelatives.Add(link);
        _db.SaveChanges();

        SelectedRelativeToAdd = null;

        LoadRelatives();
        LoadDebts();
    }

    [RelayCommand]
    private void RemoveRelative(Customer? relative)
    {
        if (SelectedCustomer is null || relative is null)
            return;

        var link = _db.CustomerRelatives.FirstOrDefault(r =>
            (r.CustomerId == SelectedCustomer.Id && r.RelativeId == relative.Id) ||
            (r.CustomerId == relative.Id && r.RelativeId == SelectedCustomer.Id));

        if (link is null)
            return;

        _db.CustomerRelatives.Remove(link);
        _db.SaveChanges();

        LoadRelatives();
        LoadDebts();
    }

    // ---------- Debt commands ----------

    [RelayCommand]
    private void AddDebt()
    {
        if (SelectedCustomer is null)
            return;

        if (!decimal.TryParse(NewDebtAmount, out var amount) || amount <= 0)
            return;

        var debt = new DebtRecord
        {
            CustomerId = SelectedCustomer.Id,
            Amount = amount,
            Description = string.IsNullOrWhiteSpace(NewDebtDescription) ? null : NewDebtDescription.Trim(),
            Date = NewDebtDate.LocalDateTime
        };

        _db.DebtRecords.Add(debt);
        _db.SaveChanges();

        LoadDebts();

        NewDebtAmount = string.Empty;
        NewDebtDescription = string.Empty;
        NewDebtDate = DateTimeOffset.Now;
    }

    [RelayCommand]
    private void DeleteDebt(DebtItemViewModel? item)
    {
        if (item is null)
            return;

        _db.DebtRecords.Remove(item.Record);
        _db.SaveChanges();

        Debts.Remove(item);
        RecalculateTotal();
    }

    [RelayCommand]
    private void TogglePaid(DebtItemViewModel? item)
    {
        if (item is null)
            return;

        var wasAlreadyPaid = item.Record.IsPaid;
        item.Record.IsPaid = !item.Record.IsPaid;

        _db.SaveChanges();

        item.RefreshFromRecord();
        RecalculateTotal();

        if (!wasAlreadyPaid && item.Record.IsPaid)
        {
            ApplyScoreChange(item.Record.CustomerId, item.Record.OverdueNotificationSent);
        }
    }

    [RelayCommand]
    private void ReduceDebt(DebtItemViewModel? item)
    {
        if (item is null)
            return;

        if (!decimal.TryParse(item.PaymentAmount, out var payment) || payment <= 0)
            return;

        if (payment > item.Record.Amount)
        {
            payment = item.Record.Amount;
        }

        item.Record.Amount -= payment;

        if (item.Record.Amount <= 0)
        {
            var wasOverdue = item.Record.OverdueNotificationSent;
            var customerId = item.Record.CustomerId;
            
            _db.DebtRecords.Remove(item.Record);
            _db.SaveChanges();

            Debts.Remove(item);
            RecalculateTotal();
            return;
        }

        _db.SaveChanges();

        item.RefreshFromRecord();
        item.PaymentAmount = string.Empty;

        RecalculateTotal();
    }
    
    private void CheckOverdueDebts()
    {
        var now = DateTime.Now;

        var overdueDebts = _db.DebtRecords
            .Include(d => d.Customer)
            .Where(d => !d.IsPaid && !d.OverdueNotificationSent)
            .ToList()
            .Where(d => now - d.Date > NotificationSettings.OverdueThreshold)
            .ToList();

        if (overdueDebts.Count == 0)
            return;

        foreach (var debt in overdueDebts)
        {
            var customerName = debt.Customer?.Name ?? "Bilinmeyen";
            var phone = string.IsNullOrWhiteSpace(debt.Customer?.Phone) ? "numarası kayıtlı olmayan" : debt.Customer!.Phone;

            var message = $"{phone} telefon numaralı {customerName} kişisinin {debt.Amount:0.##} tutarlı borcu üzerinden 1 ay geçti.";

            Notifications.Insert(0, message);
            debt.OverdueNotificationSent = true;

            if (debt.Customer is not null)
            {
                debt.Customer.Score = Math.Max(ScoringSettings.MinScore, debt.Customer.Score - ScoringSettings.BecameOverduePenalty);

                var local = _allCustomers.FirstOrDefault(c => c.Id == debt.Customer.Id);
                if (local is not null)
                {
                    local.Score = debt.Customer.Score;
                }
            }
            OnPropertyChanged(nameof(SelectedCustomerHasLowScore));
        }

        _db.SaveChanges();
        UnreadNotificationCount = Notifications.Count;
        ApplyCustomerFilter();
    }

    [RelayCommand]
    private void ToggleNotificationPanel()
    {
        IsNotificationPanelOpen = !IsNotificationPanelOpen;

        if (IsNotificationPanelOpen)
        {
            UnreadNotificationCount = 0;
        }
    }
    
    private void ApplyScoreChange(int customerId, bool wasOverdueWhenResolved)
    {
        var customer = _allCustomers.FirstOrDefault(c => c.Id == customerId);
        if (customer is null)
            return;

        customer.Score = wasOverdueWhenResolved
            ? Math.Max(ScoringSettings.MinScore, customer.Score - ScoringSettings.LatePaymentPenalty)
            : Math.Min(ScoringSettings.MaxScore, customer.Score + ScoringSettings.OnTimePaymentBonus);

        _db.SaveChanges();
        ApplyCustomerFilter();
        
        if (SelectedCustomer?.Id == customerId)
        {
            OnPropertyChanged(nameof(SelectedCustomerHasLowScore));
        }
    }
    
    public async Task BackupDatabaseAsync(Window ownerWindow)
    {
        try
        {
            var storageProvider = ownerWindow.StorageProvider;

            var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Yedek dosyasının kaydedileceği klasörü seçin",
                AllowMultiple = false
            });

            if (folders.Count == 0)
                return;

            var targetFolderPath = folders[0].Path.LocalPath;
            var sourcePath = GetDbPath();

            if (!File.Exists(sourcePath))
            {
                BackupStatusMessage = "Veritabanı dosyası bulunamadı.";
                return;
            }

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupFileName = $"creditbook_backup_{timestamp}.db";
            var destinationPath = Path.Combine(targetFolderPath, backupFileName);

            File.Copy(sourcePath, destinationPath, overwrite: true);

            BackupStatusMessage = $"Yedek alındı: {backupFileName}";
            _ = ClearBackupStatusAfterDelay();
        }
        catch (Exception ex)
        {
            BackupStatusMessage = $"Yedekleme hatası: {ex.Message}";
            _ = ClearBackupStatusAfterDelay();
        }
    }
    
    private async Task ClearBackupStatusAfterDelay()
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        BackupStatusMessage = string.Empty;
    }

    private static string GetDbPath()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CreditBook");
        return Path.Combine(folder, "creditbook.db");
    }
}