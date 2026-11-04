using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dfs.Shared;

namespace Dfs.Client.Desktop.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly IDfsClient _client;
    private readonly Action<UserInfo> _onLoginSuccess;

    [ObservableProperty]
    private string _username = "an.nguyen";

    [ObservableProperty]
    private string _password = "password123";

    [ObservableProperty]
    private string _endpoint = "192.168.10.11:7000";

    [ObservableProperty]
    private bool _rememberMe = true;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasError;

    public LoginViewModel(IDfsClient client, Action<UserInfo> onLoginSuccess)
    {
        _client = client;
        _onLoginSuccess = onLoginSuccess;
    }

    [RelayCommand]
    public async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            SetError("Vui lòng nhập tên đăng nhập.");
            return;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            SetError("Vui lòng nhập mật khẩu.");
            return;
        }

        IsLoading = true;
        HasError = false;
        ErrorMessage = null;

        try
        {
            var result = await _client.LoginAsync(Username.Trim(), Password, Endpoint.Trim());
            if (result.Success && result.User != null)
            {
                _onLoginSuccess(result.User);
            }
            else
            {
                SetError(result.ErrorMessage ?? "Đăng nhập thất bại.");
            }
        }
        catch (Exception ex)
        {
            SetError($"Lỗi kết nối tới máy chủ: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void SetError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }
}
