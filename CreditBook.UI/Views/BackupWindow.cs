using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CreditBook.Storage;
using CreditBook.Storage.Backup;
using CreditBook.UI.ViewModels;

namespace CreditBook.UI.Views;

public sealed class BackupWindow : Window
{
    private readonly MainWindowViewModel _vm;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly StackPanel _panel = new() { Spacing = 14, Margin = new Thickness(26) };
    private readonly TextBlock _status = Paragraph("");
    private readonly TextBlock _error = Paragraph("");
    private readonly TextBlock _notice = Paragraph("");
    private readonly TextBox _folder = new() { IsReadOnly = true, PlaceholderText = "Yerel yedek klasörü" };
    private readonly ComboBox _interval = new() { ItemsSource = new[] { "2 saatte bir", "3 saatte bir" }, SelectedIndex = 1, MinWidth = 170 };
    private readonly TextBox _clientId = new() { PlaceholderText = "….apps.googleusercontent.com" };
    private readonly TextBox _clientSecret = new() { PlaceholderText = "Client secret (kayıtlıysa boş bırakın)", PasswordChar = '●' };
    private readonly TextBox _recoveryKey = new() { PlaceholderText = "Kurtarma anahtarını yapıştırın", PasswordChar = '●' };
    private readonly CheckBox _keyAcknowledgment = new() { Content = Paragraph("Kurtarma anahtarını başka bir cihazda veya güvenli bir yerde sakladım."), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _cloudBackups = new() { PlaceholderText = "Önce Drive yedeklerini listeleyin", HorizontalAlignment = HorizontalAlignment.Stretch };
    private BackupService? _service;
    private bool _working;

    public BackupWindow(MainWindowViewModel vm)
    {
        _vm = vm;
        Title = "Yedekleme ve kurtarma";
        Width = 740; Height = 850; MinWidth = 560; MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _error.Foreground = Brushes.Firebrick;
        _notice.Foreground = Brushes.ForestGreen;
        _panel.Children.Add(new TextBlock { Text = "Yedekleme ve kurtarma", FontSize = 23, FontWeight = FontWeight.SemiBold });
        _panel.Children.Add(Paragraph("Yerel kopyalar bilgisayarınızda, Drive kopyaları kurtarma anahtarınızla şifreli tutulur. Otomatik yedekleme uygulama açıkken ve defter değiştiğinde çalışır."));
        _panel.Children.Add(_status);
        _panel.Children.Add(_error);
        _panel.Children.Add(_notice);

        Heading("Yerel yedekler");
        _panel.Children.Add(_folder);
        _panel.Children.Add(Buttons(ActionButton("Klasör seç…", ChooseFolderAsync), ActionButton("Şimdi yerel yedek al", LocalBackupAsync)));
        _panel.Children.Add(Paragraph("Günlük ilk değişiklikten sonra otomatik kopya alınır; otomatik yerel kopyalar 30 gün tutulur. Elle aldığınız kopyalar silinmez."));

        Heading("Google Drive bağlantısı");
        _panel.Children.Add(Paragraph("İlk kurulumda Google Cloud projesinde Drive API'yi etkinleştirin ve 'Desktop app' türünde OAuth istemcisi oluşturun. Client ID ve varsa secret değerini aşağıya girin. E-posta adresi yazmak bağlantı kurmaz; Google izni tarayıcıda verilir."));
        _panel.Children.Add(ActionButton("Google'ın masaüstü OAuth kurulum kılavuzunu aç", () => OpenBrowserAsync(new Uri("https://developers.google.com/identity/protocols/oauth2/native-app#prerequisites"))));
        Field("Google OAuth Client ID", _clientId);
        Field("Google OAuth Client secret", _clientSecret);
        Field("Otomatik Drive aralığı", _interval);
        _panel.Children.Add(ActionButton("Ayarları kaydet", async () => { await SaveOptionsAsync(); _notice.Text = "Yedekleme ayarları kaydedildi."; }));

        Heading("Kurtarma anahtarı");
        _panel.Children.Add(Paragraph("İlk bağlantıdan önce anahtar dosyasını oluşturup bu bilgisayar dışında saklayın. Yeni bilgisayarda aynı Google hesabı ve bu anahtar gerekir. Anahtar kaybolursa Drive yedekleri açılamaz."));
        _panel.Children.Add(ActionButton("Anahtar oluştur / mevcut anahtarı dosyaya aktar…", ExportKeyAsync));
        Field("Başka cihazdan kurtarma anahtarı", _recoveryKey);
        _panel.Children.Add(Paragraph("Yapıştırılan anahtar seçtiğiniz Drive yedeğini açmak için kullanılabilir. 'Bu cihaza kaydet' yeni Drive yedekleri için de bu anahtarı kullanır; eski yedekler oluşturuldukları anahtarı gerektirir."));
        _panel.Children.Add(ActionButton("Anahtarı bu cihaza kaydet", ImportKeyAsync));
        _panel.Children.Add(_keyAcknowledgment);
        _panel.Children.Add(Buttons(ActionButton("Google hesabını bağla…", ConnectAsync), ActionButton("Bağlantıyı kes", DisconnectAsync)));
        _panel.Children.Add(Paragraph("Bağlanırken açılan tarayıcıda hesabınızı seçip izni verin. İşlemi iptal etmek için bu pencereyi kapatabilirsiniz. CreditBook Yedekleri klasöründeki şifreli kopyalar hesabınızda görünür."));
        _panel.Children.Add(ActionButton("Şimdi Drive'a yedekle", CloudBackupAsync));

        Heading("Defteri geri yükle");
        _panel.Children.Add(Paragraph("Geri yükleme mevcut defterin tamamını değiştirir. Önce yedek doğrulanır ve içeriği gösterilir; yalnız onayınızdan sonra değiştirilir. Mevcut defterin güvenlik kopyası korunur."));
        _panel.Children.Add(ActionButton("Yerel yedek seç ve incele…", RestoreLocalAsync));
        _panel.Children.Add(ActionButton("Drive yedeklerini listele", ListCloudAsync));
        _panel.Children.Add(_cloudBackups);
        _panel.Children.Add(ActionButton("Seçili Drive yedeğini indir ve incele…", RestoreCloudAsync));

        var close = new Button { Content = "Kapat", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 10) };
        close.Click += (_, _) => Close();
        var layout = new DockPanel();
        DockPanel.SetDock(close, Dock.Bottom);
        layout.Children.Add(close);
        layout.Children.Add(new ScrollViewer { Content = _panel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Content = layout;
        Opened += async (_, _) => await RunAsync(async () =>
        {
            _service = await Task.Run(_vm.GetBackupManager, _lifetime.Token);
            var options = _service.Options;
            _folder.Text = options.LocalFolder;
            _clientId.Text = options.GoogleClientId;
            _interval.SelectedIndex = options.CloudIntervalHours == 2 ? 0 : 1;
        });
        Closed += (_, _) => { _lifetime.Cancel(); _clientSecret.Text = ""; _recoveryKey.Text = ""; };
    }

    private void Heading(string text) => _panel.Children.Add(new TextBlock { Text = text, FontSize = 17, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 14, 0, 0) });
    private void Field(string label, Control field) { _panel.Children.Add(new TextBlock { Text = label }); _panel.Children.Add(field); }
    private static TextBlock Paragraph(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
    private static WrapPanel Buttons(params Control[] controls)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in controls) { control.Margin = new Thickness(0, 0, 8, 6); panel.Children.Add(control); }
        return panel;
    }
    private Button ActionButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += async (_, _) => await RunAsync(action);
        return button;
    }

    private BackupService Service => _service ?? throw new InvalidOperationException("Yedekleme ayarları henüz yüklenmedi.");

    private async Task RunAsync(Func<Task> action)
    {
        if (_working || _lifetime.IsCancellationRequested) return;
        _working = true; _panel.IsEnabled = false; _error.Text = ""; _notice.Text = "";
        try { await action(); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { _error.Text = "İşlem tamamlanamadı: " + ex.Message; }
        finally
        {
            if (!_lifetime.IsCancellationRequested)
            {
                try
                {
                    if (_service is not null)
                    {
                        var status = await Task.Run(() => Service.Status, _lifetime.Token);
                        _status.Text = $"Google hesabı: {status.ConnectedEmail ?? "bağlı değil"}\nSon yerel yedek: {Time(status.LastLocalBackup)}\nSon doğrulanan Drive yedeği: {Time(status.LastCloudBackup)}" +
                            (status.CloudPending ? "\nDrive'a gönderim bekliyor." : "") +
                            (status.LastError is not null ? "\nSon yedekleme hatası: " + status.LastError : "");
                        _vm.UpdateBackupStatus();
                    }
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
                catch (Exception ex) { _error.Text = "Yedekleme durumu okunamadı: " + ex.Message; }
                _panel.IsEnabled = _service is not null;
            }
            _working = false;
        }
    }

    private static string Time(DateTimeOffset? time) => time?.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("tr-TR")) ?? "henüz yok";
    private static Task OpenBrowserAsync(Uri uri)
    {
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    private async Task ChooseFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Yerel yedek klasörü", AllowMultiple = false });
        if (folders.Count == 0) return;
        _folder.Text = folders[0].TryGetLocalPath() ?? throw new InvalidOperationException("Bu bilgisayarda bir klasör seçin.");
    }

    private async Task SaveOptionsAsync()
    {
        var options = new BackupOptions
        {
            LocalFolder = _folder.Text ?? "", CloudIntervalHours = _interval.SelectedIndex == 0 ? 2 : 3,
            GoogleClientId = _clientId.Text ?? "", GoogleClientSecret = _clientSecret.Text ?? ""
        };
        await Task.Run(() =>
        {
            if (!string.Equals(options.GoogleClientId.Trim(), Service.Options.GoogleClientId, StringComparison.Ordinal) && Service.Status.ConnectedEmail is not null)
                throw new ArgumentException("Google Client ID'yi değiştirmeden önce mevcut hesabın bağlantısını kesin.");
            Service.SaveOptions(options);
        }, _lifetime.Token);
        _clientSecret.Text = "";
    }

    private async Task ExportKeyAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Kurtarma anahtarını başka cihazda saklayın", SuggestedFileName = "CreditBook-kurtarma-anahtari.txt",
            DefaultExtension = "txt", ShowOverwritePrompt = true,
            FileTypeChoices = new[] { new FilePickerFileType("Kurtarma anahtarı") { Patterns = new[] { "*.txt" } } }
        });
        if (file is null) return;
        var destination = file.TryGetLocalPath() ?? throw new InvalidOperationException("Yerel bir dosya seçin.");
        await Task.Run(async () =>
        {
            var key = Service.InitializeRecoveryKey();
            var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using var stream = new FileStream(destination, options);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(key + Environment.NewLine), _lifetime.Token);
        }, _lifetime.Token);
        _keyAcknowledgment.IsChecked = false;
        _notice.Text = "Anahtar dosyası kaydedildi. Başka cihazda veya güvenli bir yerde sakladıktan sonra aşağıdaki kutuyu işaretleyin.";
    }

    private async Task ImportKeyAsync()
    {
        var key = _recoveryKey.Text?.Trim();
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Önce kurtarma anahtarını yapıştırın.");
        await Task.Run(() => Service.ImportRecoveryKey(key), _lifetime.Token);
        _recoveryKey.Text = ""; _keyAcknowledgment.IsChecked = false;
        _notice.Text = "Kurtarma anahtarı bu cihazın güvenli kasasına kaydedildi.";
    }

    private async Task ConnectAsync()
    {
        if (_keyAcknowledgment.IsChecked != true) throw new ArgumentException("Önce kurtarma anahtarını dosyaya aktarıp başka bir yerde sakladığınızı onaylayın.");
        if (string.IsNullOrWhiteSpace(_clientId.Text)) throw new ArgumentException("Google Cloud Desktop OAuth Client ID gerekli.");
        await Task.Run(() => Service.ExportRecoveryKey(), _lifetime.Token);
        await SaveOptionsAsync();
        _notice.Text = "Tarayıcıda Google hesabınızı seçip izni verin. Pencereyi kapatarak işlemi iptal edebilirsiniz.";
        await Task.Run(() => Service.ConnectAsync(OpenBrowserAsync, _lifetime.Token), _lifetime.Token);
        _notice.Text = "Google hesabı bağlandı. Drive'a ilk kopyayı göndermek için 'Şimdi Drive'a yedekle' seçeneğini kullanabilirsiniz.";
    }

    private async Task DisconnectAsync()
    {
        await Task.Run(() => Service.DisconnectAsync(_lifetime.Token), _lifetime.Token);
        _cloudBackups.ItemsSource = null;
        _notice.Text = "Google hesabının bu cihazdaki bağlantısı kesildi. Drive'daki yedekler korunur.";
    }

    private async Task LocalBackupAsync()
    {
        await SaveOptionsAsync(); await _vm.BackupNowAsync(false); CheckVmError();
        _notice.Text = "Elle yerel yedek alındı: " + Service.Options.LocalFolder;
    }
    private async Task CloudBackupAsync()
    {
        await SaveOptionsAsync(); await _vm.BackupNowAsync(true); CheckVmError();
        _notice.Text = "Yerel yedek alındı ve şifreli Drive yüklemesi doğrulandı.";
    }
    private void CheckVmError() { if (!string.IsNullOrEmpty(_vm.ErrorMessage)) throw new InvalidOperationException(_vm.ErrorMessage); }

    private sealed record BackupChoice(CloudBackup Backup)
    {
        public override string ToString() => $"{Backup.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm} · {(Backup.Manual ? "Elle" : "Otomatik")} · {Backup.Name}";
    }

    private async Task ListCloudAsync()
    {
        var backups = await Task.Run(() => Service.ListCloudBackupsAsync(_lifetime.Token), _lifetime.Token);
        var choices = backups.OrderByDescending(b => b.CreatedAt).Select(b => new BackupChoice(b)).ToArray();
        _cloudBackups.ItemsSource = choices;
        _cloudBackups.SelectedIndex = choices.Length == 0 ? -1 : 0;
        _notice.Text = choices.Length == 0 ? "Bu Google hesabında CreditBook yedeği bulunamadı." : $"{choices.Length} Drive yedeği bulundu. İncelemek için birini seçin.";
    }

    private async Task RestoreLocalAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "CreditBook yerel yedeğini seçin", AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("CreditBook yedeği") { Patterns = new[] { "*.db", "*.bak" } }, FilePickerFileTypes.All }
        });
        if (files.Count == 0) return;
        var source = files[0].TryGetLocalPath() ?? throw new InvalidOperationException("Yerel bir yedek dosyası seçin.");
        await InspectAndRestoreAsync(source, source, null);
    }

    private async Task RestoreCloudAsync()
    {
        if (_cloudBackups.SelectedItem is not BackupChoice selected) throw new ArgumentException("Önce Drive yedeklerini listeleyip bir yedek seçin.");
        var key = _recoveryKey.Text?.Trim();
        if (string.IsNullOrWhiteSpace(key)) key = await Task.Run(() => Service.ExportRecoveryKey(), _lifetime.Token);
        var directory = Path.Combine(Path.GetTempPath(), "creditbook-drive-restore-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var source = Path.Combine(directory, "download.db");
        try
        {
            await Task.Run(() => Service.DownloadDecryptedAsync(selected.Backup.Id, key, source, _lifetime.Token), _lifetime.Token);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(source, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            await InspectAndRestoreAsync(source, "Google Drive · " + selected.Backup.Name, selected.Backup.CreatedAt);
        }
        finally
        {
            _recoveryKey.Text = "";
            await Task.Run(() => { if (Directory.Exists(directory)) Directory.Delete(directory, true); });
        }
    }

    private async Task InspectAndRestoreAsync(string source, string label, DateTimeOffset? cloudDate)
    {
        var preview = await Task.Run(() => DatabaseRecovery.Inspect(source), _lifetime.Token);
        _lifetime.Token.ThrowIfCancellationRequested();
        var date = cloudDate?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? preview.LastModified.ToString("dd.MM.yyyy HH:mm");
        var message = $"Kaynak: {label}\nYedek tarihi: {date}\nMüşteri: {preview.CustomerCount}\nToplam açık bakiye: {preview.Balance.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"))} ₺\n\nBu yedek mevcut defterin tamamının yerine geçecek. Mevcut defterin güvenlik kopyası alınacak. Devam edilsin mi?";
        if (!await LedgerDialogs.ConfirmAsync(this, "Defterin tamamını geri yükle", message)) return;
        _lifetime.Token.ThrowIfCancellationRequested();
        await _vm.RestoreAsync(source); CheckVmError();
        _notice.Text = "Yedek geri yüklendi. Önceki defterin güvenlik kopyası korundu.";
    }
}
