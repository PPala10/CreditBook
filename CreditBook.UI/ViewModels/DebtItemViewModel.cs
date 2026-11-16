using System;
using System.Linq;
using CreditBook.Core;
namespace CreditBook.UI.ViewModels;
public sealed class DebtItemViewModel(DebtSummary debt)
{
    public int Id => debt.Id;
    public decimal Amount => debt.Amount;
    public decimal Outstanding => debt.Outstanding;
    public string? Description => debt.Description;
    public DateTime Date => debt.Date;
    public DateTime DueDate => debt.DueDate == default ? debt.Date.Date.AddDays(30) : debt.DueDate;
    public string? VoidReason => debt.VoidReason;
    public bool CanChangeDue => !debt.IsVoided;
    public bool IsOverdue => Outstanding > 0 && DateTime.Today > DueDate.Date;
    public bool CanSettle => debt.Outstanding > 0;
    public bool CanVoid => debt.CanVoid;
    public string Status => debt.IsVoided ? "İptal" : debt.Outstanding == 0 ? "Kapandı" : IsOverdue ? "Gecikmiş" : "Açık";
}
public sealed class PaymentItemViewModel(PaymentSummary payment)
{
    public int Id => payment.Id;
    public decimal Amount => payment.Amount;
    public DateTime Date => payment.Date;
    public bool CanReverse => payment.ReversedAt is null;
    public string? ReversalReason => payment.ReversalReason;
    public string AllocationText => string.Join(Environment.NewLine, payment.Allocations.Select(a => $"Borç #{a.DebtId} · {a.Description ?? "Borç kaydı"}: {a.Amount:N2} ₺"));
    public string Status => payment.ReversedAt is null ? "Tahsil edildi" : $"Geri alındı ({payment.ReversedAt:dd.MM.yyyy})";
}
