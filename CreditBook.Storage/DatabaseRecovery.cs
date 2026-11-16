using CreditBook.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CreditBook.Storage;

public record BackupPreview(int CustomerCount, decimal Balance, DateTime LastModified);

/// <summary>Validates and migrates only a temporary copy; SQLite backup commits the replacement atomically.</summary>
public static class DatabaseRecovery
{
    public static BackupPreview Inspect(string source) => WithCandidate(source, candidate =>
    {
        var ledger = new Ledger(candidate);
        var customers = ledger.GetCustomers();
        return new BackupPreview(customers.Count, customers.Sum(c => c.Balance), File.GetLastWriteTime(source));
    });

    public static string Restore(string destination, string source)
    {
        if (Path.GetFullPath(source) == Path.GetFullPath(destination))
            throw new ArgumentException("Açık veritabanı kendisinin üzerine geri yüklenemez.");
        return WithCandidate(source, candidate =>
        {
            var safety = destination + ".before-restore-" + Guid.NewGuid().ToString("N") + ".bak";
            if (File.Exists(destination)) DatabaseSetup.Backup(destination, safety);
            // BackupDatabase uses a destination transaction. All app writes are paused by the UI.
            using var input = Connection(candidate, SqliteOpenMode.ReadOnly);
            using var output = Connection(destination, SqliteOpenMode.ReadWriteCreate);
            input.Open(); output.Open();
            input.BackupDatabase(output);
            return safety;
        });
    }

    private static T WithCandidate<T>(string source, Func<string, T> use)
    {
        if (!File.Exists(source)) throw new ArgumentException("Yedek dosyası bulunamadı.");
        var folder = Path.Combine(Path.GetTempPath(), "creditbook-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var candidate = Path.Combine(folder, "candidate.db");
        try
        {
            DatabaseSetup.Backup(source, candidate);
            using (var connection = Connection(candidate, SqliteOpenMode.ReadOnly))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA integrity_check";
                if (!string.Equals(command.ExecuteScalar() as string, "ok", StringComparison.Ordinal)) throw new ArgumentException("Yedeğin bütünlük kontrolü başarısız.");
                command.CommandText = "PRAGMA foreign_key_check";
                using var reader = command.ExecuteReader();
                if (reader.Read()) throw new ArgumentException("Yedekte kopuk kayıt bağlantıları var.");
            }
            var options = new DbContextOptionsBuilder<CreditBookDbContext>().UseSqlite("Data Source=" + candidate).Options;
            using (var db = new CreditBookDbContext(options))
            {
                db.Database.OpenConnection();
                using var identity = db.Database.GetDbConnection().CreateCommand();
                identity.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Customers','DebtRecords','CustomerRelatives')";
                if (Convert.ToInt32(identity.ExecuteScalar()) != 3)
                    throw new ArgumentException("Bu dosya tanınan bir CreditBook yedeği değil.");
                var known = db.Database.GetMigrations().ToArray();
                var applied = db.Database.GetAppliedMigrations().ToArray();
                if (applied.Except(known).Any() || !applied.SequenceEqual(known.Take(applied.Length)))
                    throw new ArgumentException("Yedek daha yeni veya tanınmayan bir sürüme ait.");
            }
            _ = new Ledger(candidate); // Strict legacy adoption, migrations on disposable copy only.
            using (var db = new CreditBookDbContext(options))
            {
                db.Database.OpenConnection();
                foreach (var entity in db.Model.GetEntityTypes())
                {
                    using var command = db.Database.GetDbConnection().CreateCommand();
                    command.CommandText = $"PRAGMA table_info(\"{entity.GetTableName()}\")";
                    var actual = new HashSet<(string, string, bool)>();
                    using (var reader = command.ExecuteReader())
                        while (reader.Read()) actual.Add((reader.GetString(1), reader.GetString(2).ToUpperInvariant(), reader.GetInt32(3) != 0));
                    var table = StoreObjectIdentifier.Table(entity.GetTableName()!, null);
                    var expected = entity.GetProperties().Select(p => (p.GetColumnName(table)!, p.GetColumnType()!.ToUpperInvariant(), !p.IsNullable)).ToHashSet();
                    if (!actual.SetEquals(expected)) throw new ArgumentException("Yedek CreditBook şemasıyla uyumlu değil.");
                }
            }
            var check = new Ledger(candidate);
            foreach (var customer in check.GetCustomers()) _ = check.GetAccount(customer.Id);
            return use(candidate);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        { throw new ArgumentException("Yedek doğrulanamadı; mevcut defter değiştirilmedi. " + ex.Message, ex); }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(folder, true);
        }
    }

    private static SqliteConnection Connection(string path, SqliteOpenMode mode) =>
        new(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path), Mode = mode, Pooling = false }.ToString());
}
