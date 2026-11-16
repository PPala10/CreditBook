using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CreditBook.Storage.Backup;

namespace CreditBook.UI.ViewModels;

public partial class MainWindowViewModel
{
    private readonly bool _enableBackups;
    private readonly Func<CreditBook.Storage.Ledger, BackupService>? _backupFactory;
    private BackupService? _backup;
    private Task? _automaticBackup;
    private bool _automaticPaused;
    [ObservableProperty] private string backupStatusText = "Drive yedekleme henüz bağlanmadı.";
    public BackupService GetBackupManager() => _backup ??= _backupFactory?.Invoke(_ledger!) ?? new BackupService(_ledger!.DatabasePath);

    private void ScheduleBackup()
    {
        if (!_enableBackups || _automaticPaused || _automaticBackup is { IsCompleted: false }) return;
        _automaticBackup = RunScheduledBackupAsync();
    }
    public async Task PauseAutomaticBackupsAsync()
    {
        _automaticPaused = true;
        if (_automaticBackup is not null) await _automaticBackup;
    }
    public void ResumeAutomaticBackups() { _automaticPaused = false; ScheduleBackup(); }

    private async Task RunScheduledBackupAsync()
    {
        if (!_enableBackups) return;
        try
        {
            await Task.Run(async () => await GetBackupManager().RunDueAsync(_ledger!.GetRevision()));
            UpdateBackupStatus();
        }
        catch (Exception ex) { BackupStatusText = "Yedekleme kontrol edilemedi: " + ex.Message; }
    }

    public void UpdateBackupStatus()
    {
        if (_backup is null) return;
        var status = _backup.Status;
        BackupStatusText = $"Yerel yedek: {FormatTime(status.LastLocalBackup)} · Drive: {FormatTime(status.LastCloudBackup)}" +
            (status.ConnectedEmail is null ? " · Google hesabı bağlı değil" : $" · {status.ConnectedEmail}") +
            (status.CloudPending ? " · Gönderim bekliyor" : "") +
            (status.LastError is null ? "" : " · " + status.LastError);
    }
    private static string FormatTime(DateTimeOffset? time) => time?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "henüz yok";
    public Task BackupNowAsync(bool cloud) => BackupActionAsync(async service =>
        await service.CreateManualBackupAsync(_ledger!.GetRevision(), cloud));

    public async Task BackupActionAsync(Func<BackupService, Task> action)
    {
        if (!CanEdit) return;
        IsBusy = true; ErrorMessage = "";
        try { await Task.Run(async () => await action(GetBackupManager())); }
        catch (Exception ex) { ErrorMessage = "Yedekleme işlemi tamamlanamadı: " + ex.Message; }
        finally { UpdateBackupStatus(); IsBusy = false; }
    }
}
