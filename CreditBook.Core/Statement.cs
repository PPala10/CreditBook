namespace CreditBook.Core;

public record StatementRow(DateTime Date, string Kind, string Description, decimal Debit, decimal Credit, decimal Balance, int RecordId);
public record AccountStatement(CustomerSummary Customer, DateTime From, DateTime To,
    decimal OpeningBalance, decimal ClosingBalance, IReadOnlyList<StatementRow> Rows)
{
    public string? Warning { get; init; }
}

public static class StatementCalculator
{
    public static AccountStatement Calculate(CustomerSummary customer, IEnumerable<DebtRecord> debts,
        IEnumerable<Payment> payments, DateTime from, DateTime to)
    {
        from = from.Date; to = to.Date;
        if (from > to) throw new ArgumentException("Başlangıç tarihi bitiş tarihinden sonra olamaz.");
        var movements = new List<StatementRow>();
        var legacy = false;
        foreach (var debt in debts)
        {
            // Legacy settled debts have no reconstructible payment date: never invent one.
            if (debt.IsPaid && debt.Allocations.Count == 0) { legacy = true; continue; }
            movements.Add(new(debt.Date, "Borç", debt.Description ?? "Borç kaydı", debt.Amount, 0, 0, debt.Id));
            if (debt.IsVoided)
            {
                if (debt.VoidedAt is { } at)
                    movements.Add(new(at, "Borç iptali", debt.VoidReason ?? "Eski iptal kaydı", 0, debt.Amount, 0, debt.Id));
                else
                {
                    legacy = true;
                    movements.RemoveAt(movements.Count - 1);
                }
            }
        }
        foreach (var payment in payments)
        {
            movements.Add(new(payment.Date, "Tahsilat", $"Tahsilat #{payment.Id}", 0, payment.Amount, 0, payment.Id));
            if (payment.ReversedAt is { } at)
                movements.Add(new(at, "Tahsilat geri alma", payment.ReversalReason ?? "Eski geri alma kaydı", payment.Amount, 0, 0, payment.Id));
        }
        var opening = movements.Where(r => r.Date.Date < from).Sum(r => r.Debit - r.Credit);
        var balance = opening;
        var rows = movements.Where(r => r.Date.Date >= from && r.Date.Date <= to)
            .OrderBy(r => r.Date).ThenBy(r => r.Kind == "Borç" ? 0 : r.Kind == "Tahsilat" ? 1 : 2).ThenBy(r => r.RecordId)
            .Select(r => r with { Balance = balance += r.Debit - r.Credit }).ToList();
        return new(customer, from, to, opening, balance, rows)
        {
            Warning = legacy ? "Eski kapalı/iptal borçların ödeme veya iptal tarihi bilinmiyor. Bu kayıtlar dönem hareketlerine dahil değildir; geçmiş dönem bakiyeleri eksik olabilir." : null
        };
    }
}
