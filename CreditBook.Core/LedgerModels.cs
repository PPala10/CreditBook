namespace CreditBook.Core;

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Note { get; set; }
    public int Score { get; set; } = 100;
    public bool IsArchived { get; set; }
    public ICollection<DebtRecord> Debts { get; set; } = new List<DebtRecord>();
}

public class CustomerRelative
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int RelativeId { get; set; }
    public Customer? Relative { get; set; }
}

public class DebtRecord
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public DateTime Date { get; set; }
    // Retained for legacy settled debts whose individual payments were never recorded.
    public bool IsPaid { get; set; }
    public bool OverdueNotificationSent { get; set; }
    public bool IsVoided { get; set; }
    public ICollection<PaymentAllocation> Allocations { get; set; } = new List<PaymentAllocation>();
    public decimal Outstanding => IsPaid || IsVoided ? 0 : Amount - Allocations.Where(a => a.Payment?.ReversedAt is null).Sum(a => a.Amount);
}

public class Payment
{
    public DateTime? ReversedAt { get; set; }
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public ICollection<PaymentAllocation> Allocations { get; set; } = new List<PaymentAllocation>();
}

public class PaymentAllocation
{
    public int Id { get; set; }
    public int PaymentId { get; set; }
    public Payment? Payment { get; set; }
    public int DebtRecordId { get; set; }
    public DebtRecord? DebtRecord { get; set; }
    public decimal Amount { get; set; }
}

public record Allocation(int DebtId, decimal Amount);
public record CustomerSummary(int Id, string Name, string? Phone, int Score, decimal Balance);
public record DebtSummary(int Id, int CustomerId, string OwnerName, decimal Amount,
    decimal Outstanding, string? Description, DateTime Date, bool IsVoided, bool CanVoid);
public record PaymentSummary(int Id, decimal Amount, DateTime Date, DateTime? ReversedAt);
public record AccountSummary(CustomerSummary Customer, IReadOnlyList<DebtSummary> Debts,
    IReadOnlyList<PaymentSummary> Payments, IReadOnlyList<CustomerSummary> Relatives,
    decimal FamilyBalance);
public record OverdueSummary(int DebtId, string CustomerName, decimal Outstanding, DateTime Date);
