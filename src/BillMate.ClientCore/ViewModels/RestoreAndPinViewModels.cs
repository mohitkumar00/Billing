using System.Collections.ObjectModel;
using BillMate.Application.Common.Interfaces;
using BillMate.Application.Features.Backup;
using BillMate.ClientCore.Api;
using BillMate.ClientCore.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Refit;

namespace BillMate.ClientCore.ViewModels;

public sealed partial class RestoreWizardViewModel : StatefulViewModelBase
{
    private readonly IBillMateApi _api;

    public ObservableCollection<DriveBackupFileDto> AvailableBackups { get; } = [];

    [ObservableProperty]
    private int _currentStep = 1; // 1 = Pick Backup, 2 = Confirm & Verify, 3 = Completed

    [ObservableProperty]
    private DriveBackupFileDto? _selectedBackup;

    [ObservableProperty]
    private bool _isRestoreCompleted;

    [ObservableProperty]
    private string _statusSubtitle = "Select a backup snapshot to restore your shop database.";

    [ObservableProperty]
    private bool _isDriveConnected = true;

    public RestoreWizardViewModel(IBillMateApi api) => _api = api;

    [RelayCommand]
    public async Task LoadBackupsAsync(CancellationToken ct = default)
    {
        await SafeExecuteAsync(async () =>
        {
            CurrentStep = 1;
            IsRestoreCompleted = false;
            AvailableBackups.Clear();

            // 1. Retrieve historical/local backups
            try
            {
                var history = await _api.GetBackupHistoryAsync(30, ct);
                foreach (var h in history.Where(x => x.Status == BillMate.Domain.Enums.BackupStatus.Uploaded || x.Status == BillMate.Domain.Enums.BackupStatus.Verified))
                {
                    var isDrive = h.BackupTarget == BillMate.Domain.Enums.BackupTarget.GoogleDrive;
                    var label = isDrive ? $"☁️ [Drive] {h.FileName}" : $"💾 [Local] {h.FileName}";
                    var fileId = isDrive ? (h.RemoteFileId ?? h.Id.ToString()) : $"local:{h.RemoteFileId ?? h.FileName}";
                    AvailableBackups.Add(new DriveBackupFileDto(fileId, label, h.FileSizeBytes, h.StartedAt));
                }
            }
            catch
            {
                // Local history fetch may be empty or unconfigured initially
            }

            // 2. Fetch Google Drive backups if configured
            try
            {
                var driveList = await _api.ListDriveBackupsAsync(ct);
                IsDriveConnected = true;
                foreach (var f in driveList)
                {
                    if (!AvailableBackups.Any(x => x.FileId == f.FileId))
                    {
                        AvailableBackups.Add(new DriveBackupFileDto(f.FileId, $"☁️ {f.FileName}", f.SizeBytes, f.CreatedAtUtc));
                    }
                }
            }
            catch (ApiException apiEx) when ((int)apiEx.StatusCode == 400 || apiEx.Content?.Contains("Google Drive") == true)
            {
                IsDriveConnected = false;
            }
            catch
            {
                IsDriveConnected = false;
            }

            IsEmpty = AvailableBackups.Count == 0;
            StatusSubtitle = AvailableBackups.Count > 0
                ? $"Found {AvailableBackups.Count} database snapshot(s). Select one to restore."
                : (IsDriveConnected
                    ? "No backups found. Click 'Create Local Backup Now' to create a snapshot."
                    : "No backups found. Google Drive is not connected, but you can create a local backup below.");
        });
    }

    [RelayCommand]
    public Task LoadDriveBackupsAsync(CancellationToken ct = default) => LoadBackupsAsync(ct);

    [RelayCommand]
    public async Task CreateLocalBackupAsync(CancellationToken ct = default)
    {
        await SafeExecuteAsync(async () =>
        {
            var res = await _api.RunBackupAsync(new RunBackupBody(BillMate.Domain.Enums.BackupTarget.LocalDisk), ct);
            ToastMessage = "Local database backup created successfully!";
            await LoadBackupsAsync(ct);
        });
    }

    [RelayCommand]
    public void ProceedToConfirmation(DriveBackupFileDto backup)
    {
        SelectedBackup = backup;
        CurrentStep = 2;
    }

    [RelayCommand]
    public void CancelConfirmation()
    {
        SelectedBackup = null;
        CurrentStep = 1;
    }

    [RelayCommand]
    public async Task ExecuteRestoreAsync(CancellationToken ct = default)
    {
        if (SelectedBackup is null) return;

        await SafeExecuteAsync(async () =>
        {
            var fileId = SelectedBackup.FileId;
            if (fileId.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
            {
                var path = fileId.Substring(6);
                await _api.RestoreLocalBackupAsync(new RestoreLocalBackupCommand(path), ct);
            }
            else
            {
                await _api.RestoreFromDriveAsync(new RestoreFromDriveCommand(fileId), ct);
            }

            CurrentStep = 3;
            IsRestoreCompleted = true;
            ToastMessage = $"Successfully restored database from '{SelectedBackup.FileName}'.";
        });
    }
}

public sealed partial class PinLoginViewModel : StatefulViewModelBase
{
    private readonly IBillMateApi _api;
    private readonly ClientSessionState _session;
    private readonly INavigationService? _navigation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaskedPinDots))]
    private string _pinInput = string.Empty;

    [ObservableProperty]
    private Guid _selectedUserId;

    [ObservableProperty]
    private string _serverAddress = "http://localhost:5050";

    [ObservableProperty]
    private bool _isServerSettingsOpen;

    public string MaskedPinDots => new string('●', PinInput.Length).PadRight(4, '○');

    public PinLoginViewModel(IBillMateApi api, ClientSessionState session, INavigationService? navigation = null)
    {
        _api = api;
        _session = session;
        _navigation = navigation;
        _serverAddress = session.ServerBaseUrl;
    }

    [RelayCommand]
    public void ToggleServerSettings()
    {
        IsServerSettingsOpen = !IsServerSettingsOpen;
    }

    [RelayCommand]
    public void SaveServerAddress()
    {
        if (!string.IsNullOrWhiteSpace(ServerAddress))
        {
            _session.ServerBaseUrl = ServerAddress.Trim().TrimEnd('/');
            IsServerSettingsOpen = false;
            ToastMessage = $"Server updated to {_session.ServerBaseUrl}";
        }
    }

    [RelayCommand]
    public void AppendDigit(string digit)
    {
        if (PinInput.Length >= 6 || string.IsNullOrEmpty(digit) || !char.IsDigit(digit[0])) return;
        PinInput += digit[0];
    }

    [RelayCommand]
    public void Backspace()
    {
        if (PinInput.Length > 0) PinInput = PinInput[..^1];
    }

    [RelayCommand]
    public async Task CancelAndBackAsync()
    {
        if (_navigation != null)
        {
            await _navigation.NavigateToAsync("//pos");
        }
    }

    [RelayCommand]
    public async Task SubmitPinAsync(CancellationToken ct = default)
    {
        if (PinInput.Length < 4) return;

        var targetUserId = SelectedUserId != Guid.Empty ? (Guid?)SelectedUserId : _session.CurrentAuth?.UserId;

        try
        {
            await SafeExecuteAsync(async () =>
            {
                var auth = await _api.PinLoginAsync(new Application.Features.Auth.PinLoginCommand(targetUserId, PinInput, _session.DeviceId), ct);
                _session.CurrentAuth = auth;
                PinInput = string.Empty;
                ToastMessage = $"Welcome back, {auth.FullName}!";

                if (_navigation is not null)
                {
                    await _navigation.NavigateToAsync("//pos");
                }
            });
        }
        finally
        {
            if (HasError)
            {
                PinInput = string.Empty;
            }
        }
    }
}
