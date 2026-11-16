using CreditBook.Core;
using Microsoft.EntityFrameworkCore;

namespace CreditBook.Storage;

public sealed partial class Ledger
{
    public void UpdateCustomer(int id, string name, string phone, string? note) => Write(db =>
    {
        var customer = ActiveCustomer(db, id);
        name = name.Trim(); phone = phone.Trim();
        if (name.Length == 0) throw new ArgumentException("Müşteri adı boş olamaz.");
        if (phone.Length != 11 || phone.Any(c => c < '0' || c > '9'))
            throw new ArgumentException("Telefon numarası 11 haneli olmalı.");
        if (db.Customers.Any(c => c.Id != id && c.Phone == phone && !c.IsArchived))
            throw new ArgumentException("Bu telefon numarasıyla kayıtlı bir müşteri var.");
        customer.Name = name; customer.Phone = phone;
        customer.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        return true;
    });

    internal static string RequiredReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("İşlem gerekçesini yazın.");
        return reason.Trim();
    }

    public void ChangeDueDate(int debtId, DateTime dueDate, string reason) => Write(db =>
    {
        var debt = Debts(db).SingleOrDefault(d => d.Id == debtId) ?? throw new ArgumentException("Borç bulunamadı.");
        if (debt.IsVoided) throw new ArgumentException("İptal edilen borcun vadesi değiştirilemez.");
        reason = RequiredReason(reason);
        if (dueDate == default || dueDate.Date < debt.Date.Date) throw new ArgumentException("Vade borç tarihinden önce olamaz.");
        if (debt.DueDate.Date == dueDate.Date) return false;
        db.DueDateChanges.Add(new DueDateChange { DebtRecordId = debtId, OldDueDate = debt.DueDate,
            NewDueDate = dueDate.Date, Reason = reason, ChangedAt = Now });
        debt.DueDate = dueDate.Date;
        return true;
    });

    public AccountStatement GetStatement(int customerId, DateTime from, DateTime to)
    {
        using var db = Open();
        using var transaction = db.Database.BeginTransaction();
        var customer = Summaries(db).SingleOrDefault(c => c.Id == customerId)
            ?? throw new ArgumentException("Müşteri bulunamadı.");
        var debts = Debts(db).AsNoTracking().Where(d => d.CustomerId == customerId).ToList();
        var payments = db.Payments.AsNoTracking().Where(p => p.CustomerId == customerId).ToList();
        return StatementCalculator.Calculate(customer, debts, payments, from, to);
    }

    // Stable content revision includes timestamps/reasons/notes and all history, not file mtime (WAL).
    public string GetRevision()
    {
        using var db = Open();
        using var transaction = db.Database.BeginTransaction();
        var snapshot = new {
            Customers = db.Customers.AsNoTracking().OrderBy(x => x.Id).ToList(),
            Debts = db.DebtRecords.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id,x.CustomerId,x.Amount,x.Description,x.Date,x.DueDate,x.IsPaid,x.IsVoided,x.VoidedAt,x.VoidReason }).ToList(),
            Payments = db.Payments.AsNoTracking().OrderBy(x => x.Id).ToList(),
            Allocations = db.PaymentAllocations.AsNoTracking().OrderBy(x => x.Id).ToList(),
            Relatives = db.CustomerRelatives.AsNoTracking().OrderBy(x => x.Id).Select(x => new {x.Id,x.CustomerId,x.RelativeId}).ToList(),
            Changes = db.DueDateChanges.AsNoTracking().OrderBy(x => x.Id).Select(x => new {x.Id,x.DebtRecordId,x.OldDueDate,x.NewDueDate,x.ChangedAt,x.Reason}).ToList()
        };
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(snapshot);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(json));
    }
}
