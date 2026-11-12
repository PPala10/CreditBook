using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CreditBook.Storage;

internal static class DatabaseSetup
{
    private static readonly string[] LegacyMigrations =
    [
        "20260703123135_InitialCreate", "20260706064010_AddCustomerRelatives",
        "20260706064844_AddOverdueNotificationFlag", "20260707062214_AddCustomerScore"
    ];

    public static void Initialize(CreditBookDbContext db, string path)
    {
        db.Database.OpenConnection();
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Customers'";
        var hasCustomers = Convert.ToInt64(command.ExecuteScalar()) != 0;
        if (hasCustomers && !db.Database.GetAppliedMigrations().Any())
        {
            VerifyLegacyTable(db, "Customers",
                [("Id", "INTEGER", true, true), ("Name", "TEXT", true, false),
                 ("Phone", "TEXT", false, false), ("Note", "TEXT", false, false), ("Score", "INTEGER", true, false)], []);
            VerifyLegacyTable(db, "DebtRecords",
                [("Id", "INTEGER", true, true), ("CustomerId", "INTEGER", true, false),
                 ("Amount", "TEXT", true, false), ("Description", "TEXT", false, false),
                 ("Date", "TEXT", true, false), ("IsPaid", "INTEGER", true, false),
                 ("OverdueNotificationSent", "INTEGER", true, false)], [("CustomerId", "Customers", "Id", "CASCADE")]);
            VerifyLegacyTable(db, "CustomerRelatives",
                [("Id", "INTEGER", true, true), ("CustomerId", "INTEGER", true, false),
                 ("RelativeId", "INTEGER", true, false)],
                [("CustomerId", "Customers", "Id", "RESTRICT"), ("RelativeId", "Customers", "Id", "RESTRICT")]);
            Backup(path, path + ".before-ledger-" + Guid.NewGuid().ToString("N") + ".bak");
            using var transaction = db.Database.BeginTransaction();
            db.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (MigrationId TEXT NOT NULL PRIMARY KEY, ProductVersion TEXT NOT NULL)");
            foreach (var migration in LegacyMigrations)
                db.Database.ExecuteSql($"INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ({migration}, {"10.0.9"})");
            transaction.Commit();
        }
        db.Database.Migrate();
    }

    private static void VerifyLegacyTable(CreditBookDbContext db, string table,
        (string Name, string Type, bool Required, bool PrimaryKey)[] expected,
        (string From, string Table, string To, string OnDelete)[] expectedForeignKeys)
    {
        const string unrecognized = "Mevcut veritabanı şeması tanınmıyor. Veriler değiştirilmedi; dönüştürme için inceleme gerekiyor.";
        using var command = db.Database.GetDbConnection().CreateCommand();
        // Table is an internal constant, never user input.
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        var columns = new HashSet<(string, string, bool, bool)>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) columns.Add((reader.GetString(1), reader.GetString(2).ToUpperInvariant(), reader.GetInt32(3) != 0, reader.GetInt32(5) != 0));
        if (!columns.SetEquals(expected)) throw new InvalidOperationException(unrecognized);
        command.CommandText = $"PRAGMA foreign_key_list(\"{table}\")";
        var foreignKeys = new HashSet<(string, string, string, string)>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) foreignKeys.Add((reader.GetString(3), reader.GetString(2), reader.GetString(4), reader.GetString(6)));
        if (!foreignKeys.SetEquals(expectedForeignKeys)) throw new InvalidOperationException(unrecognized);
    }

    public static void Backup(string source, string destination)
    {
        var targetPath = Path.GetFullPath(destination);
        if (File.Exists(targetPath)) throw new ArgumentException("Bu yedek dosyası zaten var.");
        using var original = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadOnly }.ToString());
        using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = targetPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());
        original.Open(); target.Open();
        original.BackupDatabase(target);
    }
}
