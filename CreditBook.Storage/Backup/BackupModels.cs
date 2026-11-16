namespace CreditBook.Storage.Backup;

public sealed record BackupOptions
{
    public string LocalFolder { get; init; } = "";
    public int CloudIntervalHours { get; init; } = 3;
    public string GoogleClientId { get; init; } = "";
    public string GoogleClientSecret { get; init; } = "";
}
public sealed record BackupStatus(DateTimeOffset? LastLocalBackup = null, DateTimeOffset? LastCloudBackup = null,
    string? ConnectedEmail = null, string? LastError = null, bool CloudPending = false);
public sealed record CloudBackup(string Id, string Name, DateTimeOffset CreatedAt, bool Manual, string KeyId);
public interface ISecretStore
{
    string? Read(string name);
    void Write(string name, string value);
    void Delete(string name);
}
public interface IBackupCloud
{
    string? ConnectedEmail { get; }
    Task ConnectAsync(Func<Uri, Task> openBrowser, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CloudBackup>> ListAsync(CancellationToken cancellationToken = default);
    Task<CloudBackup> UploadAsync(byte[] encrypted, DateTimeOffset createdAt, bool manual, string keyId, CancellationToken cancellationToken = default);
    Task<byte[]> DownloadAsync(string id, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public static class BackupRetention
{
    public static IReadOnlyList<CloudBackup> CloudToDelete(IEnumerable<CloudBackup> backups, DateTimeOffset now)
    {
        var automatic = backups.Where(b => !b.Manual).OrderByDescending(b => b.CreatedAt).ToList();
        var keep = automatic.Where(b => b.CreatedAt >= now.AddHours(-48)).Select(b => b.Id).ToHashSet();
        KeepBuckets(automatic.Where(b=>b.CreatedAt>=now.AddDays(-30)), b=>b.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd"), keep, 30);
        KeepBuckets(automatic.Where(b=>b.CreatedAt>=now.AddDays(-84)), b=>System.Globalization.ISOWeek.GetYear(b.CreatedAt.UtcDateTime)+"-"+System.Globalization.ISOWeek.GetWeekOfYear(b.CreatedAt.UtcDateTime), keep,12);
        KeepBuckets(automatic.Where(b=>b.CreatedAt>=now.AddMonths(-12)), b=>b.CreatedAt.UtcDateTime.ToString("yyyy-MM"), keep,12);
        return automatic.Where(b=>!keep.Contains(b.Id)).ToList();
    }
    static void KeepBuckets(IEnumerable<CloudBackup> rows, Func<CloudBackup,string> bucket, HashSet<string> keep,int limit)
    { foreach(var row in rows.GroupBy(bucket).Take(limit).Select(g=>g.First())) keep.Add(row.Id); }
}
