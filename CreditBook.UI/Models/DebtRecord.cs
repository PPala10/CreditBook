using System;

namespace CreditBook.UI.Models;

public class DebtRecord
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public DateTime Date { get; set; } = DateTime.Now;
    public bool IsPaid { get; set; }
    public bool OverdueNotificationSent { get; set; }
}