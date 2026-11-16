using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CreditBook.Storage.Backup;

/// <summary>UI facade. Call RunDueAsync after changes and from a one-minute timer while the app is open.
/// Revision must be a stable content revision (including all ledger mutations), persisted across app runs.</summary>
public sealed class BackupService
{
    readonly string dbPath,statePath,secretPrefix; readonly ISecretStore secrets;readonly TimeProvider clock;readonly IBackupCloud cloud;
    readonly SemaphoreSlim operation=new(1,1); readonly object stateLock=new(); Persisted state;
    sealed record LocalCopy(string Path,DateTimeOffset CreatedAt);
    sealed record Persisted
    {
        public BackupOptions Options {get;init;}=new();public BackupStatus Status {get;init;}=new();
        public string? LocalRevision {get;init;} public string? CloudRevision {get;init;}
        public List<LocalCopy> AutomaticCopies {get;init;}=[];
    }
    public BackupOptions Options=>state.Options;
    public BackupStatus Status
    {
        get {
            try {return state.Status with {ConnectedEmail=CloudConfigured?cloud.ConnectedEmail:null};}
            catch(Exception e) when(e is InvalidOperationException or PlatformNotSupportedException or System.ComponentModel.Win32Exception) {return state.Status with {LastError=state.Status.LastError??e.Message};}
        }
    }
    bool CloudConfigured=>cloud is not GoogleDriveBackupClient||!string.IsNullOrEmpty(Options.GoogleClientId);
    public BackupService(string databasePath,HttpClient? httpClient=null,ISecretStore? secretStore=null,TimeProvider? clock=null,IBackupCloud? cloud=null)
    {
        dbPath=Path.GetFullPath(databasePath);statePath=dbPath+".backup.json";secrets=secretStore??new OsSecretStore();this.clock=clock??TimeProvider.System;
        secretPrefix=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dbPath))).Substring(0,24);
        state=File.Exists(statePath)?JsonSerializer.Deserialize<Persisted>(File.ReadAllText(statePath))??new():new();
        if(string.IsNullOrEmpty(state.Options.LocalFolder))state=state with {Options=state.Options with {LocalFolder=Path.Combine(Path.GetDirectoryName(dbPath)!,"Yedekler")}};
        this.cloud=cloud??new GoogleDriveBackupClient(httpClient??new HttpClient(),secrets,secretPrefix+".google",ClientOptions,this.clock);
    }
    BackupOptions ClientOptions()=>Options with {GoogleClientSecret=secrets.Read(secretPrefix+".client-secret")??""};
    public void SaveOptions(BackupOptions options)
    {
        if(options.CloudIntervalHours is not (2 or 3))throw new ArgumentException("Drive aralığı 2 veya 3 saat olmalı.");
        if(string.IsNullOrWhiteSpace(options.LocalFolder))throw new ArgumentException("Yerel yedek klasörü gerekli.");
        if(!string.IsNullOrEmpty(Options.GoogleClientId)&&options.GoogleClientId.Trim()!=Options.GoogleClientId&&cloud.ConnectedEmail!=null)
            throw new InvalidOperationException("OAuth istemcisini değiştirmeden önce Google hesabının bağlantısını kesin.");
        if(!string.IsNullOrWhiteSpace(options.GoogleClientSecret))secrets.Write(secretPrefix+".client-secret",options.GoogleClientSecret.Trim());
        lock(stateLock) {state=state with {Options=options with {LocalFolder=Path.GetFullPath(options.LocalFolder),GoogleClientId=options.GoogleClientId.Trim(),GoogleClientSecret=""}};Save();}
    }
    public string InitializeRecoveryKey()
    {
        var existing=secrets.Read(secretPrefix+".key");if(existing!=null)return existing;
        var key=BackupArchive.GenerateRecoveryKey();secrets.Write(secretPrefix+".key",key);return key;
    }
    public string ExportRecoveryKey()=>secrets.Read(secretPrefix+".key")??throw new InvalidOperationException("Önce kurtarma anahtarı oluşturun veya içe aktarın.");
    public void ImportRecoveryKey(string recoveryKey)
    {
        BackupArchive.KeyId(recoveryKey);secrets.Write(secretPrefix+".key",recoveryKey.Trim());
        // The next changed-only pass must upload with the newly imported key as well.
        lock(stateLock) {state=state with {CloudRevision=null};Save();}
    }
    public async Task ConnectAsync(Func<Uri,Task> openBrowser,CancellationToken cancellationToken=default)
    {
        await operation.WaitAsync(cancellationToken);
        try {await cloud.ConnectAsync(openBrowser,cancellationToken);lock(stateLock){state=state with {CloudRevision=null,Status=state.Status with {LastCloudBackup=null,CloudPending=true,LastError=null}};Save();}}
        finally {operation.Release();}
    }
    public async Task DisconnectAsync(CancellationToken cancellationToken=default)
    {
        await operation.WaitAsync(cancellationToken);
        try {await cloud.DisconnectAsync(cancellationToken);lock(stateLock){state=state with {CloudRevision=null,Status=state.Status with {LastCloudBackup=null,CloudPending=false,LastError=null}};Save();}}
        finally {operation.Release();}
    }
    public async Task RunDueAsync(string revision,CancellationToken cancellationToken=default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(revision);
        await operation.WaitAsync(cancellationToken);
        try {
            var now=clock.GetUtcNow();var errors=new List<string>();
            if(state.LocalRevision!=revision &&(state.Status.LastLocalBackup==null||TimeZoneInfo.ConvertTime(state.Status.LastLocalBackup.Value,clock.LocalTimeZone).Date<clock.GetLocalNow().Date)) {
                try {var path=CreateLocal(false,now);var snapshotRevision=new Ledger(path).GetRevision();lock(stateLock){state=state with {LocalRevision=snapshotRevision,Status=state.Status with {LastLocalBackup=now}};Save();}PruneLocal(now);}
                catch(Exception e) when(e is not OperationCanceledException) {errors.Add("Yerel yedek: "+e.Message);}
            }
            var connected=false;
            try {connected=CloudConfigured&&cloud.ConnectedEmail!=null;}
            catch(Exception e) when(e is InvalidOperationException or PlatformNotSupportedException or System.ComponentModel.Win32Exception) {errors.Add("Drive güvenli kasası: "+e.Message);}
            if(connected&&state.CloudRevision==revision&&state.Status.CloudPending)SetStatus(state.Status with {CloudPending=false});
            if(connected&&state.CloudRevision!=revision) {
                SetStatus(state.Status with {CloudPending=true});
                if(state.Status.LastCloudBackup==null||now-state.Status.LastCloudBackup.Value>=TimeSpan.FromHours(Options.CloudIntervalHours)) {
                    try {
                        var snapshotRevision=await Upload(false,now,cancellationToken);
                        lock(stateLock){state=state with {CloudRevision=snapshotRevision,Status=state.Status with {LastCloudBackup=now,CloudPending=true}};Save();}
                        SetStatus(state.Status with {CloudPending=new Ledger(dbPath).GetRevision()!=snapshotRevision});
                        foreach(var backup in BackupRetention.CloudToDelete(await cloud.ListAsync(cancellationToken),now))await cloud.DeleteAsync(backup.Id,cancellationToken);
                    }catch(Exception e) when(e is not OperationCanceledException) {errors.Add("Drive yedek: "+e.Message);}
                }
            }
            SetStatus(state.Status with {LastError=errors.Count==0?null:string.Join("\n",errors)});
        } finally {operation.Release();}
    }
    public async Task<string> CreateManualBackupAsync(string revision,bool uploadToCloud=false,CancellationToken cancellationToken=default)
    {
        await operation.WaitAsync(cancellationToken);
        try {
            var now=clock.GetUtcNow();var path=CreateLocal(true,now);var localRevision=new Ledger(path).GetRevision();
            lock(stateLock){state=state with {LocalRevision=localRevision,Status=state.Status with {LastLocalBackup=now}};Save();}
            if(uploadToCloud) {
                var cloudRevision=await Upload(true,now,cancellationToken);
                lock(stateLock){state=state with {CloudRevision=cloudRevision,Status=state.Status with {LastCloudBackup=now,CloudPending=true}};Save();}
                SetStatus(state.Status with {CloudPending=new Ledger(dbPath).GetRevision()!=cloudRevision});
            }
            return path;
        }finally {operation.Release();}
    }
    public async Task<IReadOnlyList<CloudBackup>> ListCloudBackupsAsync(CancellationToken cancellationToken=default)
    { await operation.WaitAsync(cancellationToken);try {return await cloud.ListAsync(cancellationToken);}finally {operation.Release();} }
    /// <summary>Writes the authenticated decrypted download to a new file; refuses to overwrite an existing file. Caller must validate the SQLite schema before restoring.</summary>
    public async Task DownloadDecryptedAsync(string id,string recoveryKey,string destination,CancellationToken cancellationToken=default)
    {
        await operation.WaitAsync(cancellationToken);
        try {
            var encrypted=await cloud.DownloadAsync(id,cancellationToken);var bytes=BackupArchive.Decrypt(encrypted,recoveryKey);
            try {
                await using var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,FileOptions.Asynchronous);
                try {await output.WriteAsync(bytes,cancellationToken);await output.FlushAsync(cancellationToken);}
                catch {output.Dispose();File.Delete(destination);throw;}
            }finally {CryptographicOperations.ZeroMemory(bytes);}
        }
        finally {operation.Release();}
    }
    string CreateLocal(bool manual,DateTimeOffset now)
    {
        Directory.CreateDirectory(Options.LocalFolder);
        var path=Path.Combine(Options.LocalFolder,$"creditbook-{(manual?"manual":"auto")}-{now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
        new Ledger(dbPath).Backup(path);
        if(!manual)lock(stateLock){state=state with {AutomaticCopies=[..state.AutomaticCopies,new LocalCopy(path,now)]};Save();}
        return path;
    }
    async Task<string> Upload(bool manual,DateTimeOffset now,CancellationToken ct)
    {
        var key=ExportRecoveryKey();var temporary=Path.Combine(Path.GetDirectoryName(dbPath)!,".creditbook-upload-"+Guid.NewGuid()+".db");
        try {new Ledger(dbPath).Backup(temporary);var snapshotRevision=new Ledger(temporary).GetRevision();var bytes=await File.ReadAllBytesAsync(temporary,ct);byte[] encrypted;try {encrypted=BackupArchive.Encrypt(bytes,key);}finally {CryptographicOperations.ZeroMemory(bytes);}await cloud.UploadAsync(encrypted,now,manual,BackupArchive.KeyId(key),ct);return snapshotRevision;}
        finally {if(File.Exists(temporary))File.Delete(temporary);}
    }
    void PruneLocal(DateTimeOffset now)
    {
        foreach(var copy in state.AutomaticCopies.Where(c=>c.CreatedAt<now.AddDays(-30)).ToArray()) {
            if(Path.GetFileName(copy.Path).StartsWith("creditbook-auto-",StringComparison.Ordinal)&&Path.GetExtension(copy.Path)==".db")File.Delete(copy.Path);
            lock(stateLock){state=state with {AutomaticCopies=state.AutomaticCopies.Where(c=>c!=copy).ToList()};Save();}
        }
    }
    void SetStatus(BackupStatus status) {lock(stateLock){state=state with {Status=status with {ConnectedEmail=null}};Save();}}
    void Save()
    {
        var temp=statePath+"."+Guid.NewGuid()+".tmp";
        try {File.WriteAllText(temp,JsonSerializer.Serialize(state,new JsonSerializerOptions {WriteIndented=true}));File.Move(temp,statePath,true);}
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
}
