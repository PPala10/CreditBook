# Google Drive yedek kurulumu

CreditBook çevrimdışı çalışır. Yerel SQLite yedekleri seçtiğiniz klasöre açık `.db` dosyaları olarak yazılır; klasörü bilgisayar hesabınızın izinleriyle koruyun. Drive'a yalnız AES-256-GCM ile şifrelenmiş `.cbbackup` arşivleri gönderilir. Gerçek Google hesabı yetkilendirmesi geliştirici testlerinin parçası değildir.

## Geliştirici kurulumu

1. Google Cloud projesinde **Google Drive API**'yi etkinleştirin.
2. Google Auth Platform'da uygulama adı, destek e-postası ve hedef kitleyi ayarlayın. Test modundaysa işletmenin Google hesabını test kullanıcısı ekleyin. Yayın öncesi Google'ın güncel izin/onay gerekliliklerini tamamlayın. Test modundaki dış uygulamaların refresh token'ları sınırlı ömürlü olabilir; kalıcı işletme kullanımı için yayın durumunu kontrol edin.
3. OAuth istemcisini **Desktop app** türünde oluşturun. Web, Android ve iOS istemci türleri bu döngü için uygun değildir.
4. İstemci kimliğini CreditBook yedek ayarlarına girin. Desktop istemcisinin Google tarafından verilen `client_secret` değeri varsa onu da ayarlayın; Google token uç noktasına gönderilir. Bu değer işletim sistemi kasasında saklanır, ayar JSON'una yazılmaz. Google'ın native-app belgesi bu parametreyi optional olarak tanımlar; Google projenizin verdiği değeri kullanmak istemci uyumluluğunu sağlar.
5. İzinler `openid email https://www.googleapis.com/auth/drive.file` ile sınırlıdır. Drive'ın tamamı için erişim istenmez. `drive.file` uygulamanın oluşturduğu/açtığı dosyalarda silme dahil işlem sağlar; silme yetkisinin teknik olarak kaldırıldığı vaat edilmez.

OAuth sistem tarayıcısında açılır. İstemci rastgele portta yalnız `127.0.0.1` dinler, beş dakikalık süre içinde state kontrolü ve PKCE S256 ile kod değiştirir. Google userinfo yanıtındaki doğrulanmış e-posta ve kalıcı `sub` hesap kimliği saklanır. Hesap veya OAuth istemci kimliği değiştirmek için önce bağlantı kesilmelidir. Drive dosya izni onaylanmazsa bağlantı tamamlanmış sayılmaz. Bağlantı kesme Google token iptali yapar; çevrimdışıysa işlem hata verir ve yeniden denemek için kimlik bilgisi korunur.

Drive'da görünür **CreditBook Yedekleri** klasörü oluşturulur. Dosya ve klasörler uygulama özel metadata'sıyla işaretlenir. Listeleme/indirme/silme bu işaretle ve klasör üyeliğiyle sınırlandırılır; otomatik temizlik elle yedekleri silmez. Yeni bilgisayar aynı OAuth projesi/uygulaması ve aynı Google hesabını kullanmalıdır; farklı Google Cloud projesi `drive.file` ile eski uygulamanın dosyalarını göremez.

## Kurtarma anahtarı ve yeni bilgisayar

İlk Drive yedeğinden önce kurtarma anahtarı oluşturun. Anahtar, token'lar ve varsa desktop istemci sırrı macOS Keychain, Windows Credential Manager veya Linux Secret Service kasasına yazılır. Linux'ta `secret-tool` ve çalışan Secret Service gerekir. Güvenli kasa bulunmazsa düz metin saklamaya geçilmez; yerel yedekler çalışmaya devam eder.

**Kurtarma anahtarını bilgisayar dışında güvenli bir yerde saklayın.** Dışa aktarma anahtar metnini kullanıcıya verir; uygulama otomatik olarak dosyaya veya günlüğe yazmaz. Google oturumu tek başına şifreyi çözmez. Anahtar kaybolursa arşivlerin şifresi kurtarılamaz. Anahtar değiştirmek eski yedekleri yeniden şifrelemez; eski anahtarı ilgili arşivler için saklayın. Arşivde gizli olmayan anahtar kimliği bulunur.

Yeni bilgisayarda aynı Desktop OAuth istemcisini yapılandırın, aynı Google hesabına bağlanın, kayıtlı kurtarma anahtarını içe aktarın ve Drive yedeklerini listeleyin. İndirilen arşivi ilgili anahtarla geçici bir dosyaya çözün. Geri yükleme, şema/bütünlük doğrulaması ve mevcut defterin güvenlik yedeğini alan ayrı onaylı iş akışından geçmelidir.

## Zamanlama

Uygulama açıkken günlük ilk değişiklikten sonra yerel otomatik yedek alınır. Son 30 gün saklanır. Drive aralığı varsayılan üç saat, isteğe göre iki saattir. Kaynak değişmediyse tekrar yüklenmez. Yükleme doğrulanmadan Drive başarı zamanı ilerlemez; başarısız yükleme sonraki çağrıda yeniden denenir. Yerel ve Drive başarı zamanları ayrıdır. Otomatik Drive kopyaları son 48 saat sık; son 30 gün günlük, 12 hafta haftalık ve 12 ay aylık örneklerle tutulur. Elle yedekler temizlikten muaftır.

## UI entegrasyonu

`CreditBook.Storage.Backup.BackupService`:

```csharp
var backup = new BackupService(databasePath); // Testler HttpClient, ISecretStore, TimeProvider ve IBackupCloud verebilir.
backup.SaveOptions(backup.Options with {
    LocalFolder = chosenFolder,
    CloudIntervalHours = 3, // yalnız 2 veya 3
    GoogleClientId = desktopClientId,
    GoogleClientSecret = desktopClientSecret // boş değer mevcut kasa değerini korur
});
var keyToSaveOutsideDevice = backup.InitializeRecoveryKey(); // mevcutsa aynı anahtarı döndürür
backup.ImportRecoveryKey(savedKey); // yeni cihaz; biçim kontrol edilir
var keyToDisplayOnExplicitUserRequest = backup.ExportRecoveryKey();
await backup.ConnectAsync(uri => OpenSystemBrowserAsync(uri));
await backup.RunDueAsync(stableLedgerRevision);
var localPath = await backup.CreateManualBackupAsync(stableLedgerRevision, uploadToCloud: true);
var cloudFiles = await backup.ListCloudBackupsAsync();
await backup.DownloadDecryptedAsync(cloudFiles[0].Id, suppliedRecoveryKey, temporaryRestorePath);
await backup.DisconnectAsync();
```

`Options` gizli olmayan ayarları, `Status` ise `LastLocalBackup`, `LastCloudBackup`, `ConnectedEmail`, `LastError`, `CloudPending` alanlarını verir. Anahtar otomatik oluşturulmaz: kullanıcıya dışarıda saklatma akışını ilk bulut yedeğinden önce tamamlayın. Ayarlar/zamanlar/revizyonlar defterin yanındaki `.backup.json` dosyasında atomik olarak kalıcılaşır; token ve anahtar içermez. Dosya ve defter yolu değişirse ayarların taşınması ve anahtarın yeniden içe alınması gerekir.

`RunDueAsync`'i her başarılı defter değişikliğinden sonra ve uygulama açıkken yaklaşık dakikalık timer ile çağırın. Revizyon tüm veri değişikliklerini kapsayan ve yeniden başlatmalarda aynı kalan bir içerik kimliği olmalı; yalnız çalıştırma başına rastgele kimlik veya SQLite dosyasının WAL'ı dışlayan yazma zamanı kullanılmamalı. Başarıyla kaydedilen revizyon, yerel/Drive için alınan gerçek SQLite kopyasının `Ledger.GetRevision()` değerinden hesaplanır; yedekleme sırasında yapılan sonraki değişiklikler bekleyen olarak kalır ve sonraki timer çağrısında işlenir. Aynı servis nesnesi yedek/bağlantı işlemlerini sıralar. `RunDueAsync` olağan ağ/dosya hatalarını `LastError` alanına yazar; elle/bağlantı/indirme işlemlerinde hata çağırana iletilir. Kullanıcıya **Drive doğrulandı** ifadesini yalnız `LastCloudBackup` mevcutsa gösterin. 5 MiB üzerindeki arşivler resumable yükleme oturumuyla gönderilir; kesinti durumunda sonraki deneme yeni oturum açar. Cloud yüklemesi boyut ve Drive `md5Checksum` yanıtıyla doğrulanır; AES-GCM çözme indirme bütünlüğünü ayrıca doğrular.

İndirme mevcut dosyayı üzerine yazmayı reddeder. UI geri yükleme hedefi olarak mevcut defter yolunu kullanmamalı; önce henüz bulunmayan ayrı geçici dosyaya indirmeli. Çözülmüş dosya, güvenli geri yükleme sürecine girdidir. UI tarayıcı başlatmayı, anahtar gösterme/kaydetme seçimini, timer'ı ve geri yükleme onayını yönetir.

## Resmi kaynaklar

- [Google: OAuth 2.0 for desktop apps](https://developers.google.com/identity/protocols/oauth2/native-app)
- [Google: loopback migration — desktop support](https://developers.google.com/identity/protocols/oauth2/resources/loopback-migration)
- [Google: Drive API scopes and drive.file](https://developers.google.com/workspace/drive/api/guides/api-specific-auth)
- [Google: Drive file upload](https://developers.google.com/workspace/drive/api/guides/manage-uploads)
- [Google: OpenID Connect userinfo](https://developers.google.com/identity/openid-connect/openid-connect#obtaininguserprofileinformation)
