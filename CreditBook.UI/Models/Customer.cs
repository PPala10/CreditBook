using System.Collections.Generic;

namespace CreditBook.UI.Models;

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Note { get; set; }
    public int Score { get; set; } = 100;
    public ICollection<DebtRecord> Debts { get; set; } = new List<DebtRecord>();
}