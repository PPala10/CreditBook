using CreditBook.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CreditBook.Storage;

/// <summary>Owns ledger transactions and returns detached account snapshots to callers.</summary>
public sealed class Ledger
{
    private readonly DbContextOptions<CreditBookDbContext> _options;
    private readonly TimeProvider _clock;
    public string DatabasePath { get; }

    public Ledger(string databasePath, TimeProvider? clock = null)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        _clock = clock ?? TimeProvider.System;
        _options = new DbContextOptionsBuilder<CreditBookDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString()).Options;
        using var db = Open();
        DatabaseSetup.Initialize(db, DatabasePath);
    }

    private CreditBookDbContext Open() => new(_options);
    private DateTime Now => _clock.GetLocalNow().DateTime;

    private T Write<T>(Func<CreditBookDbContext, T> operation)
    {
        using var db = Open();
        using var transaction = db.Database.BeginTransaction();
        var result = operation(db);
        db.SaveChanges();
        transaction.Commit();
        return result;
    }

    private static IQueryable<DebtRecord> Debts(CreditBookDbContext db) => db.DebtRecords
        .Include(d => d.Allocations).ThenInclude(a => a.Payment);

    private static Customer ActiveCustomer(CreditBookDbContext db, int id) =>
        db.Customers.SingleOrDefault(c => c.Id == id && !c.IsArchived)
        ?? throw new ArgumentException("Müşteri bulunamadı.");

    public int AddCustomer(string name, string phone) => Write(db =>
    {
        name = name.Trim(); phone = phone.Trim();
        if (name.Length == 0) throw new ArgumentException("Müşteri adı boş olamaz.");
        if (phone.Length != 11 || phone.Any(c => c < '0' || c > '9'))
            throw new ArgumentException("Telefon numarası 11 haneli olmalı.");
        if (db.Customers.Any(c => c.Phone == phone && !c.IsArchived))
            throw new ArgumentException("Bu telefon numarasıyla kayıtlı bir müşteri var.");
        var customer = new Customer { Name = name, Phone = phone };
        db.Customers.Add(customer);
        db.SaveChanges();
        return customer.Id;
    });

    public void ArchiveCustomer(int id) => Write(db =>
    {
        var customer = ActiveCustomer(db, id);
        if (db.DebtRecords.Any(d => d.CustomerId == id) || db.Payments.Any(p => p.CustomerId == id))
            throw new ArgumentException("İşlem geçmişi olan müşteri kaldırılamaz.");
        customer.IsArchived = true;
        db.CustomerRelatives.RemoveRange(db.CustomerRelatives.Where(r => r.CustomerId == id || r.RelativeId == id));
        return true;
    });

    public int AddDebt(int customerId, decimal amount, string? description, DateTime date) => Write(db =>
    {
        ActiveCustomer(db, customerId);
        LedgerRules.ValidateAmount(amount);
        ValidateDate(date);
        var debt = new DebtRecord { CustomerId = customerId, Amount = amount,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(), Date = date };
        db.DebtRecords.Add(debt);
        db.SaveChanges();
        return debt.Id;
    });

    public int RecordPayment(int customerId, decimal amount, DateTime date) => Write(db =>
    {
        ActiveCustomer(db, customerId);
        ValidateDate(date);
        var debts = Debts(db).Where(d => d.CustomerId == customerId).ToList();
        var allocations = LedgerRules.Allocate(debts, amount);
        if (allocations.Any(a => debts.Single(d => d.Id == a.DebtId).Date.Date > date.Date))
            throw new ArgumentException("Tahsilat tarihi, kapattığı borç tarihinden önce olamaz.");
        var payment = new Payment { CustomerId = customerId, Amount = amount, Date = date };
        foreach (var allocation in allocations)
        {
            var debt = debts.Single(d => d.Id == allocation.DebtId);
            payment.Allocations.Add(new PaymentAllocation { DebtRecord = debt, Amount = allocation.Amount });
        }
        db.Payments.Add(payment);
        db.SaveChanges();
        foreach (var debt in debts.Where(d => !d.IsPaid && d.Outstanding == 0)) debt.IsPaid = true;
        return payment.Id;
    });

    public void SettleDebt(int id) => Write(db =>
    {
        var debt = Debts(db).SingleOrDefault(d => d.Id == id)
            ?? throw new ArgumentException("Borç bulunamadı.");
        if (debt.Outstanding == 0) return false;
        if (debt.Date.Date > Now.Date) throw new ArgumentException("Gelecek tarihli borç bugün kapatılamaz.");
        var payment = new Payment { CustomerId = debt.CustomerId, Amount = debt.Outstanding, Date = Now };
        payment.Allocations.Add(new PaymentAllocation { DebtRecord = debt, Amount = debt.Outstanding });
        db.Payments.Add(payment);
        debt.IsPaid = true;
        return true;
    });

    public void ReversePayment(int id) => Write(db =>
    {
        var payment = db.Payments.Include(p => p.Allocations).ThenInclude(a => a.DebtRecord)
            .ThenInclude(d => d!.Allocations).ThenInclude(a => a.Payment)
            .SingleOrDefault(p => p.Id == id) ?? throw new ArgumentException("Tahsilat bulunamadı.");
        if (payment.ReversedAt.HasValue) return false;
        payment.ReversedAt = Now;
        foreach (var allocation in payment.Allocations)
        {
            var debt = allocation.DebtRecord!;
            debt.IsPaid = debt.Allocations.Where(a => a.Payment?.ReversedAt is null).Sum(a => a.Amount) >= debt.Amount;
        }
        return true;
    });

    public void VoidDebt(int id) => Write(db =>
    {
        var debt = Debts(db).SingleOrDefault(d => d.Id == id)
            ?? throw new ArgumentException("Borç bulunamadı.");
        if (!LedgerRules.CanVoid(debt))
            throw new ArgumentException("Yalnızca tahsilat geçmişi olmayan açık borç iptal edilebilir.");
        debt.IsVoided = true;
        return true;
    });

    public void LinkRelative(int first, int second) => Write(db =>
    {
        if (first == second) throw new ArgumentException("Müşteri kendisine bağlanamaz.");
        ActiveCustomer(db, first); ActiveCustomer(db, second);
        if (!db.CustomerRelatives.Any(r => (r.CustomerId == first && r.RelativeId == second)
            || (r.CustomerId == second && r.RelativeId == first)))
            db.CustomerRelatives.Add(new CustomerRelative { CustomerId = Math.Min(first, second), RelativeId = Math.Max(first, second) });
        return true;
    });

    public void UnlinkRelative(int first, int second) => Write(db =>
    {
        db.CustomerRelatives.RemoveRange(db.CustomerRelatives.Where(r =>
            (r.CustomerId == first && r.RelativeId == second) || (r.CustomerId == second && r.RelativeId == first)));
        return true;
    });

    private IReadOnlyList<CustomerSummary> Summaries(CreditBookDbContext db)
    {
        var debts = Debts(db).AsNoTracking().ToList().ToLookup(d => d.CustomerId);
        return db.Customers.AsNoTracking().Where(c => !c.IsArchived).OrderBy(c => c.Name).ToList()
            .Select(c => new CustomerSummary(c.Id, c.Name, c.Phone, LedgerRules.Score(debts[c.Id], Now),
                debts[c.Id].Sum(d => d.Outstanding))).ToList();
    }

    public IReadOnlyList<CustomerSummary> GetCustomers()
    {
        using var db = Open();
        return Summaries(db);
    }

    public AccountSummary GetAccount(int id)
    {
        using var db = Open();
        using var transaction = db.Database.BeginTransaction();
        var customers = Summaries(db);
        var customer = customers.SingleOrDefault(c => c.Id == id)
            ?? throw new ArgumentException("Müşteri bulunamadı.");
        var links = db.CustomerRelatives.Where(r => r.CustomerId == id || r.RelativeId == id).ToList();
        var relativeIds = links.Select(r => r.CustomerId == id ? r.RelativeId : r.CustomerId).ToHashSet();
        var relatives = customers.Where(c => relativeIds.Contains(c.Id)).ToList();
        var debts = Debts(db).AsNoTracking().Where(d => d.CustomerId == id).OrderBy(d => d.Date).ThenBy(d => d.Id).ToList()
            .Select(d => new DebtSummary(d.Id, id, customer.Name, d.Amount, d.Outstanding, d.Description, d.Date, d.IsVoided, LedgerRules.CanVoid(d))).ToList();
        var payments = db.Payments.AsNoTracking().Where(p => p.CustomerId == id).OrderByDescending(p => p.Date)
            .Select(p => new PaymentSummary(p.Id, p.Amount, p.Date, p.ReversedAt)).ToList();
        return new AccountSummary(customer, debts, payments, relatives, customer.Balance + relatives.Sum(r => r.Balance));
    }

    public IReadOnlyList<OverdueSummary> GetOverdue()
    {
        using var db = Open();
        var cutoff = Now.AddDays(-30);
        return Debts(db).Include(d => d.Customer).AsNoTracking().Where(d => !d.IsVoided && d.Date < cutoff)
            .ToList().Where(d => d.Outstanding > 0)
            .Select(d => new OverdueSummary(d.Id, d.Customer!.Name, d.Outstanding, d.Date)).ToList();
    }

    public void Backup(string destination)
    {
        if (Path.GetFullPath(destination) == DatabasePath)
            throw new ArgumentException("Yedek dosyası mevcut veritabanının üzerine yazılamaz.");
        DatabaseSetup.Backup(DatabasePath, destination);
    }

    private void ValidateDate(DateTime date)
    {
        if (date == default || date.Date > Now.Date) throw new ArgumentException("İşlem tarihi bugün veya geçmişte olmalı.");
    }
}
