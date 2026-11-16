using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Dfs.Shared.Client;
using Dfs.Shared.Messages;

namespace Dfs.Client.Desktop.ViewModels;

public sealed class LoginViewModel : ViewModelBase
{
    private readonly IDfsClient _client;
    private readonly Action<LoginResponse> _onLoginSuccess;

    private string _username = "demo";
    private string _password = "demo";
    private string _errorMessage = "";
    private bool _isBusy;

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            SetProperty(ref _errorMessage, value);
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public ICommand LoginCommand { get; }

    public LoginViewModel(IDfsClient client, Action<LoginResponse> onLoginSuccess)
    {
        _client = client;
        _onLoginSuccess = onLoginSuccess;
        LoginCommand = new RelayCommand(async () => await ExecuteLoginAsync());
    }

    private async Task ExecuteLoginAsync()
    {
        if (IsBusy) return;

        ErrorMessage = "";
        IsBusy = true;

        try
        {
            var response = await _client.LoginAsync(Username.Trim(), Password);
            _onLoginSuccess(response);
        }
        catch (DfsException ex)
        {
            ErrorMessage = ex.Code switch
            {
                ErrorCode.Unauthorized => "Tên đăng nhập hoặc mật khẩu không chính xác.",
                ErrorCode.Forbidden => "Tài khoản của bạn đã bị vô hiệu hóa bởi Quản trị viên.",
                _ => $"Lỗi đăng nhập: {ex.Message} ({ex.Code})"
            };
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Không thể kết nối máy chủ Metadata: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
