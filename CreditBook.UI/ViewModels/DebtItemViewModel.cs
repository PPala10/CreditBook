using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CreditBook.UI.Models;

namespace CreditBook.UI.ViewModels;

public partial class DebtItemViewModel : ObservableObject
{
    public DebtRecord Record { get; }
    public string OwnerName { get; }

    public DebtItemViewModel(DebtRecord record, string ownerName)
    {
        Record = record;
        OwnerName = ownerName;
    }

    public decimal Amount => Record.Amount;
    public string? Description => Record.Description;
    public DateTime Date => Record.Date;
    public bool IsPaid => Record.IsPaid;

    [ObservableProperty]
    private string paymentAmount = string.Empty;

    public void RefreshFromRecord()
    {
        OnPropertyChanged(nameof(Amount));
        OnPropertyChanged(nameof(IsPaid));
    }
}