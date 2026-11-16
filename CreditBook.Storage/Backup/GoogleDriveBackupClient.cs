using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CreditBook.Storage.Backup;

public sealed class GoogleDriveBackupClient : IBackupCloud
{
    const string Api="https://www.googleapis.com/drive/v3/files";
    const string Marker="creditbook-backup-v1";
    readonly HttpClient http; readonly ISecretStore secrets; readonly string secretName; readonly Func<BackupOptions> options; readonly TimeProvider clock;
    Token? token; string? folderId;
    sealed record Token(string AccessToken,string RefreshToken,DateTimeOffset ExpiresAt,string Email,string Subject);
    public string? ConnectedEmail=>LoadToken()?.Email;
    public GoogleDriveBackupClient(HttpClient http,ISecretStore secrets,string secretName,Func<BackupOptions> options,TimeProvider? clock=null)
    { this.http=http;this.secrets=secrets;this.secretName=secretName;this.options=options;this.clock=clock??TimeProvider.System; }
    Token? LoadToken()=>token??=secrets.Read(secretName) is { } value?JsonSerializer.Deserialize<Token>(value):null;
    void SaveToken(Token value) { secrets.Write(secretName,JsonSerializer.Serialize(value));token=value; }
    public async Task ConnectAsync(Func<Uri,Task> openBrowser,CancellationToken cancellationToken=default)
    {
        var o=options(); if(string.IsNullOrWhiteSpace(o.GoogleClientId))throw new InvalidOperationException("Google Desktop OAuth istemci kimliği gerekli.");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);timeout.CancelAfter(TimeSpan.FromMinutes(5));var ct=timeout.Token;
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        try {
            var redirect="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/callback/";
            var verifier=Base64Url(RandomNumberGenerator.GetBytes(32));var state=Base64Url(RandomNumberGenerator.GetBytes(32));
            var fields=new Dictionary<string,string> { ["client_id"]=o.GoogleClientId,["redirect_uri"]=redirect,["response_type"]="code",["scope"]="openid email https://www.googleapis.com/auth/drive.file",["access_type"]="offline",["prompt"]="consent select_account",["code_challenge_method"]="S256",["code_challenge"]=Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),["state"]=state };
            await openBrowser(new Uri("https://accounts.google.com/o/oauth2/v2/auth?"+Query(fields)));
            string? code=null;
            while(code==null) {
                using var client=await listener.AcceptTcpClientAsync(ct);using var stream=client.GetStream();using var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);
                var line=await reader.ReadLineAsync(ct);var path=line?.Split(' ').ElementAtOrDefault(1);
                var uri=Uri.TryCreate("http://127.0.0.1"+path,UriKind.Absolute,out var parsed)?parsed:null;
                var query=uri==null?new Dictionary<string,string>():ParseQuery(uri.Query);
                var valid=uri?.AbsolutePath=="/callback/"&&query.GetValueOrDefault("state")==state;
                var response=Encoding.UTF8.GetBytes(valid?"HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nConnection: close\r\n\r\nCreditBook: Bu pencereyi kapatabilirsiniz.":"HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\nInvalid callback");
                await stream.WriteAsync(response,ct);
                if(!valid)continue;
                if(query.ContainsKey("error"))throw new InvalidOperationException("Google hesabına bağlantı onaylanmadı.");
                code=query.GetValueOrDefault("code"); if(string.IsNullOrEmpty(code))throw new InvalidOperationException("Google yetkilendirme kodu eksik.");
            }
            var exchange=new Dictionary<string,string> { ["client_id"]=o.GoogleClientId,["code"]=code,["redirect_uri"]=redirect,["grant_type"]="authorization_code",["code_verifier"]=verifier };
            if(!string.IsNullOrEmpty(o.GoogleClientSecret))exchange["client_secret"]=o.GoogleClientSecret;
            using var json=await TokenRequest(exchange,ct);var root=json.RootElement;
            if(!root.TryGetProperty("scope",out var grantedScope)||!(grantedScope.GetString()??"").Split(' ',StringSplitOptions.RemoveEmptyEntries).Contains("https://www.googleapis.com/auth/drive.file",StringComparer.Ordinal))
                throw new InvalidOperationException("Google Drive dosya izni verilmedi; yeniden bağlanıp yedek iznini onaylayın.");
            var access=root.GetProperty("access_token").GetString()!;
            using var identityRequest=new HttpRequestMessage(HttpMethod.Get,"https://openidconnect.googleapis.com/v1/userinfo");identityRequest.Headers.Authorization=new AuthenticationHeaderValue("Bearer",access);
            using var identityResponse=await http.SendAsync(identityRequest,ct); await EnsureSuccess(identityResponse,ct);
            using var identity=JsonDocument.Parse(await identityResponse.Content.ReadAsStringAsync(ct));var user=identity.RootElement;
            if(!user.TryGetProperty("email_verified",out var verified)||!verified.GetBoolean())throw new InvalidOperationException("Google e-posta adresi doğrulanamadı.");
            var subject=user.GetProperty("sub").GetString()!; var email=user.GetProperty("email").GetString()!;
            if(LoadToken() is { } previous &&previous.Subject!=subject)throw new InvalidOperationException("Başka Google hesabına geçmeden önce mevcut hesabın bağlantısını kesin.");
            var refresh=root.TryGetProperty("refresh_token",out var refreshValue)?refreshValue.GetString():LoadToken()?.RefreshToken;
            if(string.IsNullOrEmpty(refresh))throw new InvalidOperationException("Google çevrimdışı erişim vermedi; yeniden onaylayın.");
            SaveToken(new Token(access,refresh,clock.GetUtcNow().AddSeconds(root.GetProperty("expires_in").GetInt32()),email,subject));folderId=null;
        } finally {listener.Stop();}
    }
    public async Task DisconnectAsync(CancellationToken cancellationToken=default)
    {
        var existing=LoadToken();
        // Revoke first: if offline, preserve credentials so the user can retry revocation.
        if(existing!=null) {using var response=await http.PostAsync("https://oauth2.googleapis.com/revoke",new FormUrlEncodedContent(new Dictionary<string,string>{{"token",existing.RefreshToken}}),cancellationToken);if(!response.IsSuccessStatusCode &&response.StatusCode!=HttpStatusCode.BadRequest)await EnsureSuccess(response,cancellationToken);}
        secrets.Delete(secretName);token=null;folderId=null;
    }
    async Task<string> AccessToken(CancellationToken ct,bool force=false)
    {
        var t=LoadToken()??throw new InvalidOperationException("Önce Google hesabını bağlayın.");if(!force&&t.ExpiresAt>clock.GetUtcNow().AddMinutes(1))return t.AccessToken;
        var o=options();var fields=new Dictionary<string,string> { ["client_id"]=o.GoogleClientId,["refresh_token"]=t.RefreshToken,["grant_type"]="refresh_token" };if(!string.IsNullOrEmpty(o.GoogleClientSecret))fields["client_secret"]=o.GoogleClientSecret;
        using var json=await TokenRequest(fields,ct);var r=json.RootElement;
        SaveToken(t with { AccessToken=r.GetProperty("access_token").GetString()!,RefreshToken=r.TryGetProperty("refresh_token",out var refreshed)?refreshed.GetString()??t.RefreshToken:t.RefreshToken,ExpiresAt=clock.GetUtcNow().AddSeconds(r.GetProperty("expires_in").GetInt32()) });return token!.AccessToken;
    }
    async Task<JsonDocument> TokenRequest(Dictionary<string,string> fields,CancellationToken ct)
    { using var response=await http.PostAsync("https://oauth2.googleapis.com/token",new FormUrlEncodedContent(fields),ct);await EnsureSuccess(response,ct);return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); }
    async Task<HttpResponseMessage> Send(Func<HttpRequestMessage> factory,CancellationToken ct)
    {
        for(var attempt=0;attempt<2;attempt++) {
            using var request=factory();request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",await AccessToken(ct,attempt>0));
            var response=await http.SendAsync(request,ct);
            if(response.StatusCode==HttpStatusCode.Unauthorized&&attempt==0){response.Dispose();continue;}
            try {await EnsureSuccess(response,ct);return response;}catch {response.Dispose();throw;}
        }
        throw new InvalidOperationException("Google yetkilendirmesi yenilenemedi.");
    }
    static async Task EnsureSuccess(HttpResponseMessage response,CancellationToken ct)
    { if(!response.IsSuccessStatusCode) { await response.Content.LoadIntoBufferAsync(ct);throw new HttpRequestException("Google isteği başarısız ("+(int)response.StatusCode+").",null,response.StatusCode); } }
    async Task<string> Folder(CancellationToken ct)
    {
        if(folderId!=null)return folderId;
        var query="trashed = false and mimeType = 'application/vnd.google-apps.folder' and appProperties has { key='creditbook' and value='"+Marker+"' }";
        using var response=await Send(()=>new HttpRequestMessage(HttpMethod.Get,Api+"?q="+Uri.EscapeDataString(query)+"&fields=files(id)&pageSize=100"),ct);
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));var files=json.RootElement.GetProperty("files");
        if(files.GetArrayLength()>0)return folderId=files[0].GetProperty("id").GetString()!;
        using var created=await Send(()=>new HttpRequestMessage(HttpMethod.Post,Api+"?fields=id") {Content=JsonContent(new {name="CreditBook Yedekleri",mimeType="application/vnd.google-apps.folder",appProperties=new Dictionary<string,string>{{"creditbook",Marker}}})},ct);
        using var result=JsonDocument.Parse(await created.Content.ReadAsStringAsync(ct));return folderId=result.RootElement.GetProperty("id").GetString()!;
    }
    public async Task<IReadOnlyList<CloudBackup>> ListAsync(CancellationToken cancellationToken=default)
    {
        var folder=await Folder(cancellationToken); var rows=new List<CloudBackup>();string? page=null;
        do {
            var query="trashed = false and '"+folder.Replace("'","\\'")+"' in parents and appProperties has { key='creditbook' and value='"+Marker+"' }";
            var url=Api+"?q="+Uri.EscapeDataString(query)+"&pageSize=1000&fields=nextPageToken,files(id,name,appProperties)"+(page==null?"":"&pageToken="+Uri.EscapeDataString(page));
            using var response=await Send(()=>new HttpRequestMessage(HttpMethod.Get,url),cancellationToken);using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            foreach(var file in json.RootElement.GetProperty("files").EnumerateArray()) if(ParseBackup(file) is { } row)rows.Add(row);
            page=json.RootElement.TryGetProperty("nextPageToken",out var next)?next.GetString():null;
        }while(page!=null);
        return rows.OrderByDescending(b=>b.CreatedAt).ToList();
    }
    public async Task<CloudBackup> UploadAsync(byte[] encrypted,DateTimeOffset createdAt,bool manual,string keyId,CancellationToken cancellationToken=default)
    {
        var folder=await Folder(cancellationToken);var name=$"creditbook-{createdAt:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.cbbackup";
        var metadata=new {name,parents=new[]{folder},appProperties=new Dictionary<string,string>{{"creditbook",Marker},{"createdAt",createdAt.ToString("O")},{"manual",manual?"true":"false"},{"keyId",keyId}}};
        const string uploadUrl="https://www.googleapis.com/upload/drive/v3/files";
        HttpResponseMessage response;
        if(encrypted.Length>5*1024*1024) {
            using var session=await Send(()=> {
                var request=new HttpRequestMessage(HttpMethod.Post,uploadUrl+"?uploadType=resumable&fields=id,name,size,md5Checksum,appProperties") {Content=JsonContent(metadata)};
                request.Headers.TryAddWithoutValidation("X-Upload-Content-Type","application/octet-stream");
                request.Headers.TryAddWithoutValidation("X-Upload-Content-Length",encrypted.LongLength.ToString(System.Globalization.CultureInfo.InvariantCulture));return request;
            },cancellationToken);
            var location=session.Headers.Location;
            if(location==null||!location.IsAbsoluteUri||location.Scheme!="https"||!(location.Host=="googleapis.com"||location.Host.EndsWith(".googleapis.com",StringComparison.OrdinalIgnoreCase)))throw new IOException("Drive yükleme adresi doğrulanamadı.");
            response=await Send(()=>new HttpRequestMessage(HttpMethod.Put,location) {Content=new ByteArrayContent(encrypted) {Headers={ContentType=new MediaTypeHeaderValue("application/octet-stream")}}},cancellationToken);
        }else {
            response=await Send(()=> {
                var multi=new MultipartContent("related");multi.Add(JsonContent(metadata));var media=new ByteArrayContent(encrypted);media.Headers.ContentType=new MediaTypeHeaderValue("application/octet-stream");multi.Add(media);
                return new HttpRequestMessage(HttpMethod.Post,uploadUrl+"?uploadType=multipart&fields=id,name,size,md5Checksum,appProperties") {Content=multi};
            },cancellationToken);
        }
        using(response) {
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));var root=json.RootElement;
            if(root.GetProperty("md5Checksum").GetString()!=Convert.ToHexString(MD5.HashData(encrypted)).ToLowerInvariant()||root.GetProperty("size").GetString()!=encrypted.LongLength.ToString(System.Globalization.CultureInfo.InvariantCulture))throw new IOException("Drive yükleme bütünlüğü doğrulanamadı.");
            return ParseBackup(root)??throw new IOException("Drive yedek bilgisi doğrulanamadı.");
        }
    }
    public async Task<byte[]> DownloadAsync(string id,CancellationToken cancellationToken=default)
    {
        await ValidateOwned(id,false,cancellationToken);
        using var response=await Send(()=>new HttpRequestMessage(HttpMethod.Get,Api+"/"+Uri.EscapeDataString(id)+"?alt=media"),cancellationToken);return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }
    public async Task DeleteAsync(string id,CancellationToken cancellationToken=default)
    {
        await ValidateOwned(id,true,cancellationToken);
        using var response=await Send(()=>new HttpRequestMessage(HttpMethod.Delete,Api+"/"+Uri.EscapeDataString(id)),cancellationToken);
    }
    async Task ValidateOwned(string id,bool deleting,CancellationToken ct)
    {
        var folder=await Folder(ct);
        using var response=await Send(()=>new HttpRequestMessage(HttpMethod.Get,Api+"/"+Uri.EscapeDataString(id)+"?fields=id,name,trashed,parents,appProperties"),ct);using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));var file=json.RootElement;var row=ParseBackup(file);
        if(row==null||(deleting&&row.Manual)||file.GetProperty("trashed").GetBoolean()||!file.GetProperty("parents").EnumerateArray().Any(p=>p.GetString()==folder))throw new InvalidOperationException("Bu işlem yalnızca uygulamanın uygun yedeklerinde yapılabilir.");
    }
    static CloudBackup? ParseBackup(JsonElement file)
    {
        if(!file.TryGetProperty("appProperties",out var p)||!p.TryGetProperty("creditbook",out var marker)||marker.GetString()!=Marker||!p.TryGetProperty("createdAt",out var date)||!DateTimeOffset.TryParse(date.GetString(),System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind,out var created)||!p.TryGetProperty("manual",out var manual)||manual.GetString() is not ("true" or "false")||!p.TryGetProperty("keyId",out var key))return null;
        return new CloudBackup(file.GetProperty("id").GetString()!,file.GetProperty("name").GetString()!,created,manual.GetString()=="true",key.GetString()!);
    }
    static StringContent JsonContent(object value)=>new(JsonSerializer.Serialize(value),Encoding.UTF8,"application/json");
    static string Base64Url(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
    static string Query(Dictionary<string,string> values)=>string.Join("&",values.Select(pair=>Uri.EscapeDataString(pair.Key)+"="+Uri.EscapeDataString(pair.Value)));
    static Dictionary<string,string> ParseQuery(string query)
    {
        var values=new Dictionary<string,string>();
        foreach(var pair in query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries)) {
            var parts=pair.Split('=',2);var key=Uri.UnescapeDataString(parts[0]);
            // Duplicate callback parameters must not bypass state validation.
            if(values.ContainsKey(key))return [];
            values[key]=Uri.UnescapeDataString((parts.ElementAtOrDefault(1)??"").Replace('+',' '));
        }
        return values;
    }
}
