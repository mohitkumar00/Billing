using CommunityToolkit.Mvvm.ComponentModel;
using Refit;

namespace BillMate.ClientCore.ViewModels;

/// <summary>
/// Enforces the 5 mandatory UI states (Ideal, Loading, Empty, Error, Edge/Overflow) across all ViewModels.
/// </summary>
public abstract partial class StatefulViewModelBase : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdeal))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdeal))]
    private bool _isEmpty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdeal))]
    private bool _hasError;

    [ObservableProperty]
    private string? _errorCode;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _toastMessage;

    public bool IsIdeal => !IsLoading && !HasError && !IsEmpty;

    protected void ClearError()
    {
        HasError = false;
        ErrorCode = null;
        ErrorMessage = null;
    }

    protected void SetError(string code, string message)
    {
        HasError = true;
        ErrorCode = code;
        ErrorMessage = message;
    }

    protected async Task SafeExecuteAsync(Func<Task> action, bool showLoading = true)
    {
        ClearError();
        if (showLoading) IsLoading = true;
        try
        {
            await action();
        }
        catch (ApiException apiEx)
        {
            SetError($"HTTP_{(int)apiEx.StatusCode}", string.IsNullOrWhiteSpace(apiEx.Content) ? apiEx.Message : apiEx.Content);
        }
        catch (Exception ex)
        {
            SetError("CLIENT_ERR", ex.Message);
        }
        finally
        {
            if (showLoading) IsLoading = false;
        }
    }
}
