# CreditBook: kayıt yönetimi ve güvenli yedek

Kullanıcıyla grilling görüşmesinde onaylanan kapsam: önceki önerilerin 1, 3, 4, 5, 6 numaraları. İşletme özeti ve telefon senkronizasyonu kapsam dışı. .NET 10, Avalonia, EF Core, SQLite ve çevrimdışı tek bilgisayardan düzenleme korunur.

## Kayıt ve hesap kuralları

- Müşterinin adı, telefonu ve notu düzenlenebilir. Kimlik ve hareketler korunur; aktif müşterilerde telefon benzersizdir. Eski hareketler güncel adla gösterilir.
- Borç başına vade bulunur; varsayılan borç tarihi +30 gündür. Eski kayıtlar aynı kuralla taşınır. Vade günü gecikme sayılmaz; takip eden gün başlar. Puan da vadeyi kullanır.
- Vade değişiklikleri eski/yeni tarih, gerekçe ve zamanla korunur. İptal ve tahsilat geri almada gerekçe zorunludur. Tahsilatın her borca dağılımı görülebilir.
- Dönem ekstresi açılış bakiyesi, borç, tahsilat, iptal, tahsilat geri alma ve kapanış bakiyesini içerir. Ocak tahsilatı şubatta geri alınırsa ocak kaydı korunur; şubata pozitif ters hareket eklenir. Kayıtlar silinmez. Eski ödemesi bilinmeyen kapalı borçlar için açıkça etiketli devir/düzeltme gerekir; olmayan ödeme tarihi icat edilmez.
- CSV, PDF ve yazdırma aynı ekstre hesabını kullanır. Tarih aralığı günleri kapsayıcıdır; açılış bakiyesi başlangıçtan önceki hareketlerin toplamıdır.
- Borç listesi durum, tarih aralığı; tarih, vade, tutar sıralaması sunar. Müşteriler ad/bakiye ile sıralanır. Filtreler hesap toplamını değiştirmez.

## Yedek ve kurtarma

- Tutarlı SQLite backup; seçilebilir yerel klasör, günlük ilk değişiklik sonrası yerel kopya, 30 günlük otomatik yerel saklama. Elle yedekler temizlenmez.
- İşletme kendi Google hesabını OAuth ile bağlar. Sadece e-posta adresi yetki sağlamaz. Sınırlı drive.file erişimi ve görünür CreditBook Yedekleri klasörü kullanılır.
- Varsayılan Drive aralığı 3 saat, ayardan 2 saat seçilebilir. Yalnızca veri değiştiğinde, uygulama açıkken. Bağlantı yokken bekleyen yedek sonraki denemede yüklenir. Başarı ancak yükleme doğrulanınca gösterilir; yerel ve harici başarı ayrılır.
- Buluta gitmeden AES-GCM şifreleme, işletim sisteminin güvenli kasasında erişim bilgileri ve anahtar; kullanıcı cihaz dışında kurtarma anahtarını saklar. Anahtar olmadan çözülemez. Yeni bilgisayarda aynı Google hesabı ve kurtarma anahtarıyla listeleme/indirme/geri yükleme mümkündür.
- Harici saklama: son 48 saatte sık kopyalar, sonra 30 günlük /12 haftalık /12 aylık örnekler. Yerel/Drive elle alınan yedekler temizlikten muaftır. Google Drive drive.file izni silmeyi teknik olarak da içerdiği için S3 için konuşulan silme yetkisi ayrımı Drive'da vaat edilmez; yalnız uygulamanın otomatik yedekleri temizlenir.
- Geri yükleme tüm defteri değiştirir; birleştirme yoktur. Önce geçici kopyada bütünlük, yabancı anahtarlar, şema ve migration uyumluluğu doğrulanır. Mevcut defterin güvenlik kopyası alınır; kullanıcı kaynak/tarih ve değiştirme işlemini onaylar. İşlemler durdurulur; başarısızlıkta mevcut defter korunur.
- Gerçek Google hesabı yetkilendirmesi kullanıcının tarayıcıda yapacağı adımdır. Geliştirici Google Cloud Desktop OAuth istemcisi oluşturmalıdır; kimlik bilgisi kodda sabitlenmez. Gerçek bağlantı yapılmadıkça canlı Drive yüklemesi doğrulanmış sayılmaz.

## Uygulayıcı kararları

Tasarım kontrolü: tahsilat geçmişi olan borçlarda iptal engeli korunur. Ödeme/iptal tarihi bilinmeyen eski kapalı/iptal borçlar dönem hareketlerinden çıkarılır ve ekstrede geçmiş bakiyelerin eksik olabileceği açıkça belirtilir; dengelemek için sahte ödeme tarihi kaydedilmez. Bunun bedeli eski defterlerin tarihsel ekstresinin tam olmamasıdır.

Core hesap ve ekstre modellerini taşır; Storage işlem ve sorguları sunar. Yedekleme/Drive ve dışa aktarma odaklı modüllerde kalır, ViewModel hesap kuralı kopyalamaz. Native PDF görüntüleyicide yazdırma kullanılır. Testler geçici SQLite dosyaları, sahte saat ve HTTP yanıtlarıyla çalışır; kullanıcı defterine veya hesabına dokunmaz. Kullanıcının mevcut output/imagegen dosyaları korunur.
