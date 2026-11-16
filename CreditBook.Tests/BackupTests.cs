using CreditBook.Storage;
using CreditBook.Storage.Backup;
using System.Security.Cryptography;
using System.Net;
using System.Text;
using System.Text.Json;

public static class BackupTests
{
    public static void Run(Action<string, Action> test)
    {
        test("Encrypted backup roundtrips and rejects tampering and wrong recovery key", () => {
            var key = BackupArchive.GenerateRecoveryKey();
            var data = System.Text.Encoding.UTF8.GetBytes("SQLite database bytes");
            var archive = BackupArchive.Encrypt(data, key);
            Assert(BackupArchive.Decrypt(archive, key).SequenceEqual(data));
            Reject(() => BackupArchive.Decrypt(archive, BackupArchive.GenerateRecoveryKey()));
            archive[^1] ^= 1;
            Reject(() => BackupArchive.Decrypt(archive, key));
        });
        test("Retention preserves manual and 48-hour frequent backups and keeps one old daily sample", () => {
            var now = DateTimeOffset.Parse("2026-11-16T12:00:00Z");
            var rows = new[] { new CloudBackup("manual","m",now.AddYears(-2),true,"k"),new CloudBackup("recent","r",now.AddHours(-47),false,"k"),new CloudBackup("a","a",now.AddDays(-3),false,"k"),new CloudBackup("b","b",now.AddDays(-3).AddHours(-1),false,"k"),new CloudBackup("expired","old",now.AddMonths(-13),false,"k") };
            var deleted=BackupRetention.CloudToDelete(rows,now).Select(x=>x.Id).ToArray();
            Assert(deleted.SequenceEqual(new[]{"b","expired"}));
        });
        test("Daily local backup records successful revision, persists state and leaves manual files untouched", () => {
            var dir=Path.Combine(Path.GetTempPath(),"creditbook-backup-test-"+Guid.NewGuid()); Directory.CreateDirectory(dir);
            try {
                var db=Path.Combine(dir,"ledger.db"); var ledger=new Ledger(db); ledger.AddCustomer("Ali","05551234567");
                var clock=new TestClock(DateTimeOffset.Parse("2026-11-16T12:00:00Z"));
                var secrets=new MemorySecrets(); var service=new BackupService(db,new HttpClient(),secrets,clock);
                service.SaveOptions(new BackupOptions { LocalFolder=Path.Combine(dir,"backups") });
                service.RunDueAsync(ledger.GetRevision()).GetAwaiter().GetResult(); Assert(service.Status.LastLocalBackup==clock.GetUtcNow());
                ledger.AddCustomer("Ayşe","05551234568");clock.Now=clock.Now.AddHours(1); service.RunDueAsync(ledger.GetRevision()).GetAwaiter().GetResult(); Assert(service.Status.LastLocalBackup==clock.Now.AddHours(-1));
                var reopened=new BackupService(db,new HttpClient(),secrets,clock); Assert(reopened.Status.LastLocalBackup==clock.Now.AddHours(-1));
                clock.Now=clock.Now.AddDays(1); reopened.RunDueAsync(ledger.GetRevision()).GetAwaiter().GetResult(); Assert(reopened.Status.LastLocalBackup==clock.Now);
                var manual=reopened.CreateManualBackupAsync(ledger.GetRevision()).GetAwaiter().GetResult();
                ledger.AddCustomer("Veli","05551234569");clock.Now=clock.Now.AddDays(31);reopened.RunDueAsync(ledger.GetRevision()).GetAwaiter().GetResult();
                Assert(File.Exists(manual));Assert(Directory.GetFiles(Path.Combine(dir,"backups"),"*.db").Length==2);
            } finally { Directory.Delete(dir,true); }
        });
        test("Offline cloud upload remains pending across restart then retries and honors changed-only interval", () => {
            var dir=Path.Combine(Path.GetTempPath(),"creditbook-cloud-test-"+Guid.NewGuid());Directory.CreateDirectory(dir);
            try {
                var db=Path.Combine(dir,"ledger.db");var ledger=new Ledger(db);var customer=ledger.AddCustomer("Ali","05551234567");
                var clock=new TestClock(DateTimeOffset.Parse("2026-11-16T12:00:00Z"));var secrets=new MemorySecrets();var cloud=new FakeCloud { Offline=true };
                var service=new BackupService(db,new HttpClient(),secrets,clock,cloud);service.InitializeRecoveryKey();
                service.RunDueAsync(ledger.GetRevision()).GetAwaiter().GetResult();Assert(service.Status.LastCloudBackup==null);Assert(service.Status.CloudPending);Assert(service.Status.LastLocalBackup==clock.Now);
                cloud.Offline=false;service=new BackupService(db,new HttpClient(),secrets,clock,cloud);service.RunDueAsync(ledger.GetRevision()).GetAwaiter().GetResult();Assert(service.Status.LastCloudBackup==clock.Now);Assert(!service.Status.CloudPending);
                Assert(BackupArchive.Decrypt(cloud.Archives.Single(),service.ExportRecoveryKey()).Take(15).SequenceEqual(System.Text.Encoding.ASCII.GetBytes("SQLite format 3")));
                ledger.UpdateCustomer(customer,"Ayşe","05551234567",null);clock.Now=clock.Now.AddHours(2);service.RunDueAsync(ledger.GetRevision()).GetAwaiter().GetResult();Assert(cloud.Archives.Count==1);Assert(service.Status.CloudPending);
                ledger.UpdateCustomer(customer,"Ali","05551234567",null);service.RunDueAsync(ledger.GetRevision()).GetAwaiter().GetResult();Assert(!service.Status.CloudPending);
                ledger.UpdateCustomer(customer,"Ayşe","05551234567",null);service.RunDueAsync(ledger.GetRevision()).GetAwaiter().GetResult();Assert(service.Status.CloudPending);
                cloud.Connected=false;service.SaveOptions(service.Options with {GoogleClientId="client-a"});cloud.Connected=true;
                var optionsRejected=false;try {service.SaveOptions(service.Options with {GoogleClientId="client-b"});}catch(InvalidOperationException) {optionsRejected=true;}
                Assert(optionsRejected);Assert(service.Options.GoogleClientId=="client-a");
                clock.Now=clock.Now.AddHours(1);service.RunDueAsync(ledger.GetRevision()).GetAwaiter().GetResult();Assert(cloud.Archives.Count==2);Assert(!service.Status.CloudPending);
                clock.Now=clock.Now.AddHours(4);service.RunDueAsync(ledger.GetRevision()).GetAwaiter().GetResult();Assert(cloud.Archives.Count==2);
                var key=service.ExportRecoveryKey();var otherDb=Path.Combine(dir,"new-machine.db");new Ledger(otherDb);
                var newMachine=new BackupService(otherDb,new HttpClient(),secrets,clock,cloud);newMachine.ImportRecoveryKey(key);Assert(newMachine.ExportRecoveryKey()==key);
                var restored=Path.Combine(dir,"download.db");newMachine.DownloadDecryptedAsync("backup",key,restored).GetAwaiter().GetResult();Assert(new Ledger(restored).GetCustomers().Single().Name=="Ali");
                var original=File.ReadAllBytes(restored);var blocked=false;
                try {newMachine.DownloadDecryptedAsync("backup",key,restored).GetAwaiter().GetResult();}catch(IOException) {blocked=true;}
                Assert(blocked);Assert(File.ReadAllBytes(restored).SequenceEqual(original));
            }finally {Directory.Delete(dir,true);}
        });
        test("Concurrent edits record the exact backup snapshot revision and keep later edits pending", () => {
            var dir=Path.Combine(Path.GetTempPath(),"creditbook-concurrent-backup-"+Guid.NewGuid());Directory.CreateDirectory(dir);
            try {
                var db=Path.Combine(dir,"ledger.db");var ledger=new Ledger(db);var customer=ledger.AddCustomer("Ali","05551234567");var observed=ledger.GetRevision();
                ledger.UpdateCustomer(customer,"Before upload","05551234567",null);
                var clock=new TestClock(DateTimeOffset.Parse("2026-11-16T12:00:00Z"));var cloud=new FakeCloud();var service=new BackupService(db,new HttpClient(),new MemorySecrets(),clock,cloud);service.InitializeRecoveryKey();
                cloud.DuringUpload=()=>ledger.UpdateCustomer(customer,"After upload","05551234567",null);
                service.RunDueAsync(observed).GetAwaiter().GetResult();cloud.DuringUpload=null;
                service.RunDueAsync(ledger.GetRevision()).GetAwaiter().GetResult();Assert(service.Status.CloudPending);
                clock.Now=clock.Now.AddHours(3);ledger.UpdateCustomer(customer,"Before upload","05551234567",null);
                service.RunDueAsync(ledger.GetRevision()).GetAwaiter().GetResult();Assert(cloud.Archives.Count==1);Assert(!service.Status.CloudPending);
                ledger.UpdateCustomer(customer,"After upload","05551234567",null);service.RunDueAsync(ledger.GetRevision()).GetAwaiter().GetResult();Assert(cloud.Archives.Count==2);Assert(!service.Status.CloudPending);
                clock.Now=clock.Now.AddDays(1);ledger.UpdateCustomer(customer,"Before upload","05551234567",null);service.RunDueAsync(ledger.GetRevision()).GetAwaiter().GetResult();
                Assert(Directory.GetFiles(service.Options.LocalFolder,"creditbook-auto-*.db").Length==1);
            }finally {Directory.Delete(dir,true);}
        });
        test("Automatic cloud upload leaves offline ledger editing available until the network returns", () => OfflineEditingDuringUpload().GetAwaiter().GetResult());
        test("Google desktop OAuth validates state and PKCE, refreshes tokens and restricts Drive writes", () => GoogleFlow().GetAwaiter().GetResult());
    }
    static void Assert(bool value,[System.Runtime.CompilerServices.CallerLineNumber]int line=0) { if(!value) throw new Exception("Backup assertion failed at BackupTests.cs:"+line); }
    static void Reject(Action action) { try { action(); } catch(CryptographicException) { return; } throw new Exception("Expected cryptographic rejection"); }
    static async Task OfflineEditingDuringUpload()
    {
        var directory=Path.Combine(Path.GetTempPath(),"creditbook-offline-edit-"+Guid.NewGuid());Directory.CreateDirectory(directory);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CreditBook.UI.ViewModels.MainWindowViewModel? vm=null;
        try {
            var ledger=new Ledger(Path.Combine(directory,"ledger.db"));var customer=ledger.AddCustomer("Ali","05551234567");
            var cloud=new FakeCloud {UploadGate=release.Task};
            var service=new BackupService(ledger.DatabasePath,new HttpClient(),new MemorySecrets(),new TestClock(DateTimeOffset.Parse("2026-11-16T12:00:00Z")),cloud);
            service.InitializeRecoveryKey();
            vm=new CreditBook.UI.ViewModels.MainWindowViewModel(()=>ledger,enableBackups:true,backupFactory:_=>service);
            await vm.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await cloud.UploadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert(!release.Task.IsCompleted);Assert(vm.CanEdit);Assert(!vm.IsBusy);
            vm.NewDebtAmount="25";vm.NewDebtDescription="Bağlantı beklerken satış";
            await vm.AddDebtCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));
            Assert(!release.Task.IsCompleted);Assert(vm.CanEdit);Assert(!vm.IsBusy);
            Assert(ledger.GetAccount(customer).Debts.Single().Amount==25m);
            Assert(ledger.GetAccount(customer).Customer.Balance==25m);
        }finally {
            release.TrySetResult();
            if(vm is not null)await vm.PauseAutomaticBackupsAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Directory.Delete(directory,true);
        }
    }
    static async Task GoogleFlow()
    {
        var clock=new TestClock(DateTimeOffset.Parse("2026-11-16T12:00:00Z"));var secrets=new MemorySecrets();
        var handler=new GoogleHttp();using var http=new HttpClient(handler);
        var client=new GoogleDriveBackupClient(http,secrets,"test-google",()=>new BackupOptions {GoogleClientId="desktop-client",GoogleClientSecret="desktop-secret"},clock);
        Task? callback=null;
        Func<Uri,Task> browser=uri=> {
            var query=Parse(uri.Query);Assert(query["scope"]=="openid email https://www.googleapis.com/auth/drive.file");Assert(query["code_challenge_method"]=="S256");handler.Challenge=query["code_challenge"];
            callback=Task.Run(async()=> {
                using var local=new HttpClient();var redirect=query["redirect_uri"];
                using var bad=await local.GetAsync(redirect+"?state=wrong&code=bad");Assert(bad.StatusCode==HttpStatusCode.BadRequest);
                using var good=await local.GetAsync(redirect+"?state="+Uri.EscapeDataString(query["state"])+"&code=auth-code");Assert(good.IsSuccessStatusCode);
            });return Task.CompletedTask;
        };
        await client.ConnectAsync(browser);
        await callback!;Assert(client.ConnectedEmail=="shop@example.com");
        client=new GoogleDriveBackupClient(http,secrets,"test-google",()=>new BackupOptions {GoogleClientId="desktop-client",GoogleClientSecret="desktop-secret"},clock);
        Assert(client.ConnectedEmail=="shop@example.com");
        clock.Now=clock.Now.AddHours(2);var key=BackupArchive.GenerateRecoveryKey();var encrypted=BackupArchive.Encrypt(Encoding.UTF8.GetBytes("sqlite snapshot"),key);
        var uploaded=await client.UploadAsync(encrypted,clock.Now,true,BackupArchive.KeyId(key));Assert(uploaded.Manual);
        Assert(Encoding.UTF8.GetString(BackupArchive.Decrypt(await client.DownloadAsync(uploaded.Id),key))=="sqlite snapshot");
        var refused=false;try {await client.DeleteAsync(uploaded.Id);}catch(InvalidOperationException) {refused=true;}Assert(refused);
        handler.Manual=false;handler.Foreign=true;refused=false;try {await client.DeleteAsync(uploaded.Id);}catch(InvalidOperationException) {refused=true;}Assert(refused);
        Assert(handler.Refreshed);
        handler.Foreign=false;
        var large=BackupArchive.Encrypt(new byte[6*1024*1024],key);
        var largeBackup=await client.UploadAsync(large,clock.Now,false,BackupArchive.KeyId(key));Assert(!largeBackup.Manual);
        Assert(BackupArchive.Decrypt(await client.DownloadAsync(largeBackup.Id),key).Length==6*1024*1024);
        handler.Scope="openid email";var missingScopeRejected=false;
        try {await client.ConnectAsync(browser);}catch(InvalidOperationException) {missingScopeRejected=true;}
        await callback!;Assert(missingScopeRejected);Assert(client.ConnectedEmail=="shop@example.com");
    }
    static Dictionary<string,string> Parse(string query)=>query.TrimStart('?').Split('&').Select(p=>p.Split('=',2)).ToDictionary(p=>Uri.UnescapeDataString(p[0]),p=>Uri.UnescapeDataString(p[1].Replace('+',' ')));
    sealed class GoogleHttp : HttpMessageHandler
    {
        public string Challenge="";public string Scope="openid email https://www.googleapis.com/auth/drive.file";public bool Refreshed;public bool Manual=true;public bool Foreign;byte[] bytes=[];JsonElement properties;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            var uri=request.RequestUri!;
            if(uri.Host=="oauth2.googleapis.com") {
                var form=Parse(await request.Content!.ReadAsStringAsync(cancellationToken));Assert(form["client_id"]=="desktop-client"&&form["client_secret"]=="desktop-secret");
                if(form["grant_type"]=="authorization_code") {Assert(form["code"]=="auth-code");Assert(Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"]))).TrimEnd('=').Replace('+','-').Replace('/','_')==Challenge);}
                else {Assert(form["refresh_token"]=="refresh-fixture");Refreshed=true;}
                return Json(new {access_token="access-fixture",refresh_token="refresh-fixture",expires_in=3600,token_type="Bearer",scope=Scope});
            }
            Assert(request.Headers.Authorization?.Parameter=="access-fixture");
            if(uri.Host=="openidconnect.googleapis.com")return Json(new {sub="google-subject-123",email="shop@example.com",email_verified=true});
            if(uri.Query.Contains("uploadType=resumable")) {
                using var metadata=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));properties=metadata.RootElement.GetProperty("appProperties").Clone();
                var response=new HttpResponseMessage(HttpStatusCode.OK);response.Headers.Location=new Uri("https://www.googleapis.com/upload/session");return response;
            }
            if(uri.AbsolutePath=="/upload/session") {
                Assert(request.Method==HttpMethod.Put);bytes=await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                return Json(new {id="backup-id",name="backup.cbbackup",size=bytes.Length.ToString(),md5Checksum=Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(),appProperties=properties});
            }
            if(uri.AbsolutePath.Contains("/upload/")) {
                var content=(MultipartContent)request.Content!;using var metadata=JsonDocument.Parse(await content.First().ReadAsStringAsync(cancellationToken));properties=metadata.RootElement.GetProperty("appProperties").Clone();
                bytes=await content.Last().ReadAsByteArrayAsync(cancellationToken);
                if(bytes.Length>5*1024*1024)return new HttpResponseMessage(HttpStatusCode.RequestEntityTooLarge);
                return Json(new {id="backup-id",name="backup.cbbackup",size=bytes.Length.ToString(),md5Checksum=Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(),appProperties=properties});
            }
            if(uri.AbsolutePath.EndsWith("/backup-id")) {
                if(uri.Query.Contains("alt=media"))return new HttpResponseMessage(HttpStatusCode.OK) {Content=new ByteArrayContent(bytes)};
                var props=JsonSerializer.Deserialize<Dictionary<string,string>>(properties.GetRawText())!;props["manual"]=Manual?"true":"false";if(Foreign)props["creditbook"]="unrelated-app";
                return Json(new {id="backup-id",name="backup.cbbackup",trashed=false,parents=new[]{"folder-id"},appProperties=props});
            }
            if(uri.Query.Contains("files(id)"))return Json(new {files=new[]{new {id="folder-id"}}});
            throw new Exception("Unexpected Google request: "+uri);
        }
        static HttpResponseMessage Json(object body)=>new(HttpStatusCode.OK) {Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json")};
    }
    sealed class FakeCloud : IBackupCloud
    {
        public bool Offline;public Action? DuringUpload;public bool Connected=true;public Task? UploadGate;public TaskCompletionSource UploadStarted=new(TaskCreationOptions.RunContinuationsAsynchronously); public List<byte[]> Archives=[]; public string? ConnectedEmail=>Connected?"backup@example.com":null;
        public Task ConnectAsync(Func<Uri,Task> browser,CancellationToken ct=default)=>Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct=default)=>Task.CompletedTask;
        public Task<IReadOnlyList<CloudBackup>> ListAsync(CancellationToken ct=default)=>Task.FromResult<IReadOnlyList<CloudBackup>>([]);
        public async Task<CloudBackup> UploadAsync(byte[] bytes,DateTimeOffset created,bool manual,string key,CancellationToken ct=default) {if(Offline)throw new HttpRequestException("offline");Archives.Add(bytes);DuringUpload?.Invoke();UploadStarted.TrySetResult();if(UploadGate is not null)await UploadGate.WaitAsync(ct);return new CloudBackup(Guid.NewGuid().ToString(),"backup",created,manual,key);}
        public Task<byte[]> DownloadAsync(string id,CancellationToken ct=default)=>Task.FromResult(Archives[0]);
        public Task DeleteAsync(string id,CancellationToken ct=default)=>Task.CompletedTask;
    }
    sealed class TestClock(DateTimeOffset now) : TimeProvider { public DateTimeOffset Now=now; public override DateTimeOffset GetUtcNow()=>Now; }
    sealed class MemorySecrets : ISecretStore { readonly Dictionary<string,string> values=[]; public string? Read(string name)=>values.GetValueOrDefault(name); public void Write(string name,string value)=>values[name]=value; public void Delete(string name)=>values.Remove(name); }
}
