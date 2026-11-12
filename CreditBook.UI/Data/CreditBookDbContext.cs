using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using CreditBook.UI.Models;

namespace CreditBook.UI.Data;

public class CreditBookDbContext : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<DebtRecord> DebtRecords => Set<DebtRecord>();
    public DbSet<CustomerRelative> CustomerRelatives => Set<CustomerRelative>();

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

    private static string GetDatabasePath()
    {
        var applicationDataFolder = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);
        var databaseFolder = Path.Combine(applicationDataFolder, "CreditBook");

        Directory.CreateDirectory(databaseFolder);

        return Path.Combine(databaseFolder, "creditbook.db");
    }
}
