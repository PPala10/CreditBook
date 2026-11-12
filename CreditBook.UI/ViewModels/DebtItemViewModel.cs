using System;
using CreditBook.Core;
namespace CreditBook.UI.ViewModels;
public sealed class DebtItemViewModel(DebtSummary debt)
{
    public int Id => debt.Id;
    public decimal Amount => debt.Amount;
    public decimal Outstanding => debt.Outstanding;
    public string? Description => debt.Description;
    public DateTime Date => debt.Date;
    public bool CanSettle => debt.Outstanding > 0;
    public bool CanVoid => debt.CanVoid;
    public string Status => debt.IsVoided ? "İptal" : debt.Outstanding == 0 ? "Kapandı" : "Açık";
}
public sealed class PaymentItemViewModel(PaymentSummary payment)
{
    public int Id => payment.Id;
    public decimal Amount => payment.Amount;
    public DateTime Date => payment.Date;
    public bool CanReverse => payment.ReversedAt is null;
    public string Status => payment.ReversedAt is null ? "Tahsil edildi" : $"Geri alındı ({payment.ReversedAt:dd.MM.yyyy})";
}
