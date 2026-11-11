using System.Collections.ObjectModel;
using BillMate.Application.Common.Interfaces;
using BillMate.Application.Features.Backup;
using BillMate.ClientCore.Api;
using BillMate.ClientCore.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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

    public RestoreWizardViewModel(IBillMateApi api) => _api = api;

    [RelayCommand]
    public async Task LoadDriveBackupsAsync(CancellationToken ct = default)
    {
        await SafeExecuteAsync(async () =>
        {
            CurrentStep = 1;
            IsRestoreCompleted = false;
            var list = await _api.ListDriveBackupsAsync(ct);
            AvailableBackups.Clear();
            foreach (var f in list) AvailableBackups.Add(f);
            IsEmpty = AvailableBackups.Count == 0;
        });
    }

    [RelayCommand]
    public void ProceedToConfirmation(DriveBackupFileDto backup)
    {
        SelectedBackup = backup;
        CurrentStep = 2;
    }

    [RelayCommand]
    public async Task ExecuteRestoreAsync(CancellationToken ct = default)
    {
        if (SelectedBackup is null) return;

        await SafeExecuteAsync(async () =>
        {
            await _api.RestoreFromDriveAsync(new RestoreFromDriveCommand(SelectedBackup.FileId), ct);
            CurrentStep = 3;
            IsRestoreCompleted = true;
            ToastMessage = $"Restored database from '{SelectedBackup.FileName}'.";
        });
    }
}

public sealed partial class PinLoginViewModel : StatefulViewModelBase
{
    private readonly IBillMateApi _api;
    private readonly ClientSessionState _session;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaskedPinDots))]
    private string _pinInput = string.Empty;

    [ObservableProperty]
    private Guid _selectedUserId;

    public string MaskedPinDots => new string('●', PinInput.Length).PadRight(4, '○');

    public PinLoginViewModel(IBillMateApi api, ClientSessionState session)
    {
        _api = api;
        _session = session;
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
    public async Task SubmitPinAsync(CancellationToken ct = default)
    {
        if (PinInput.Length < 4 || SelectedUserId == Guid.Empty) return;

        await SafeExecuteAsync(async () =>
        {
            var auth = await _api.PinLoginAsync(new Application.Features.Auth.PinLoginCommand(SelectedUserId, PinInput, _session.DeviceId), ct);
            _session.CurrentAuth = auth;
            PinInput = string.Empty;
            ToastMessage = $"Welcome back, {auth.FullName}!";
        });
    }
}
