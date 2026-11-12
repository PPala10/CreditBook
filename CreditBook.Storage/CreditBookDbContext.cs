using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using CreditBook.Core;

namespace CreditBook.Storage;

public class CreditBookDbContext : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<DebtRecord> DebtRecords => Set<DebtRecord>();
    public DbSet<CustomerRelative> CustomerRelatives => Set<CustomerRelative>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();

    public CreditBookDbContext()
    {
    }

    public CreditBookDbContext(DbContextOptions<CreditBookDbContext> options)
        : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite($"Data Source={GetDatabasePath()}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<DebtRecord>().Ignore(d => d.Outstanding);
        modelBuilder.Entity<Payment>().HasOne(p => p.Customer).WithMany().HasForeignKey(p => p.CustomerId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PaymentAllocation>().HasOne(a => a.Payment).WithMany(p => p.Allocations).HasForeignKey(a => a.PaymentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PaymentAllocation>().HasOne(a => a.DebtRecord).WithMany(d => d.Allocations).HasForeignKey(a => a.DebtRecordId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Customer>()
            .HasMany(c => c.Debts)
            .WithOne(d => d.Customer)
            .HasForeignKey(d => d.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CustomerRelative>()
            .HasOne(r => r.Customer)
            .WithMany()
            .HasForeignKey(r => r.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CustomerRelative>()
            .HasOne(r => r.Relative)
            .WithMany()
            .HasForeignKey(r => r.RelativeId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public static string GetDatabasePath()
    {
        var overridePath = Environment.GetEnvironmentVariable("CREDITBOOK_DB_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath)) return Path.GetFullPath(overridePath);
        var applicationDataFolder = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);
        var databaseFolder = Path.Combine(applicationDataFolder, "CreditBook");

        Directory.CreateDirectory(databaseFolder);

        return Path.Combine(databaseFolder, "creditbook.db");
    }
}
