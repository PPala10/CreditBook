namespace CreditBook.Core;

public static class LedgerRules
{
    public static bool CanVoid(DebtRecord debt) => !debt.IsPaid && !debt.IsVoided && debt.Allocations.Count == 0;

    public static void ValidateAmount(decimal amount)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            throw new ArgumentException("Tutar sıfırdan büyük olmalı ve en fazla iki ondalık basamak içermeli.");
    }

    public static IReadOnlyList<Allocation> Allocate(IEnumerable<DebtRecord> debts, decimal amount)
    {
        ValidateAmount(amount);
        var open = debts.Where(d => d.Outstanding > 0).OrderBy(d => d.Date).ThenBy(d => d.Id).ToList();
        if (amount > open.Sum(d => d.Outstanding))
            throw new ArgumentException("Tahsilat, müşterinin kalan borcundan fazla olamaz.");

        var result = new List<Allocation>();
        foreach (var debt in open)
        {
            if (amount == 0) break;
            var applied = Math.Min(amount, debt.Outstanding);
            result.Add(new Allocation(debt.Id, applied));
            amount -= applied;
        }
        return result;
    }

    public static int Score(IEnumerable<DebtRecord> debts, DateTime now)
    {
        var score = 100;
        foreach (var debt in debts.Where(d => !d.IsVoided))
        {
            var due = debt.Date.AddDays(30);
            if (debt.Outstanding > 0)
            {
                if (now > due) score -= 10;
            }
            else if (debt.Allocations.Count > 0)
            {
                var settled = debt.Allocations.Where(a => a.Payment?.ReversedAt is null).Max(a => a.Payment?.Date ?? debt.Date);
                score += settled > due ? -25 : 5;
            }
        }
        return Math.Clamp(score, 0, 100);
    }
}
