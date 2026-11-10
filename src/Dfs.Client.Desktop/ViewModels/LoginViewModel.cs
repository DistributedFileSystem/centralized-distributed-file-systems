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
    private string _endpoint = "127.0.0.1:7000";
    private bool _rememberMe = true;
    private bool _isPasswordRevealed;
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

    public string Endpoint
    {
        get => _endpoint;
        set => SetProperty(ref _endpoint, value);
    }

    public bool RememberMe
    {
        get => _rememberMe;
        set => SetProperty(ref _rememberMe, value);
    }

    public bool IsPasswordRevealed
    {
        get => _isPasswordRevealed;
        set
        {
            if (SetProperty(ref _isPasswordRevealed, value))
            {
                OnPropertyChanged(nameof(PasswordChar));
                OnPropertyChanged(nameof(RevealButtonText));
            }
        }
    }

    public char PasswordChar => _isPasswordRevealed ? '\0' : '•';
    public string RevealButtonText => _isPasswordRevealed ? "🙈 Ẩn" : "👁️ Hiện";

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
    public ICommand FillDemoAccountCommand { get; }
    public ICommand TogglePasswordRevealCommand { get; }
    public ICommand DismissErrorCommand { get; }

    public LoginViewModel(IDfsClient client, Action<LoginResponse> onLoginSuccess)
    {
        _client = client;
        _onLoginSuccess = onLoginSuccess;

        LoginCommand = new RelayCommand(async () => await ExecuteLoginAsync());
        FillDemoAccountCommand = new RelayCommand(() =>
        {
            Username = "demo";
            Password = "demo";
            ErrorMessage = "";
        });
        TogglePasswordRevealCommand = new RelayCommand(() => IsPasswordRevealed = !IsPasswordRevealed);
        DismissErrorCommand = new RelayCommand(() => ErrorMessage = "");
    }

    private async Task ExecuteLoginAsync()
    {
        if (IsBusy) return;

        ErrorMessage = "";

        if (string.IsNullOrWhiteSpace(Username))
        {
            ErrorMessage = "Vui lòng nhập tên đăng nhập.";
            return;
        }

        if (string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "Vui lòng nhập mật khẩu.";
            return;
        }

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
                ErrorCode.Forbidden => "Tài khoản của bạn đã bị Quản trị viên vô hiệu hóa.",
                ErrorCode.Unavailable => $"Máy chủ Metadata tại {_endpoint} hiện không khả dụng. Vui lòng kiểm tra lại dịch vụ.",
                _ => $"Lỗi xác thực: {ex.Message} ({ex.Code})"
            };
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Không thể kết nối tới máy chủ Metadata tại {_endpoint}. Vui lòng kiểm tra lại mạng LAN hoặc tường lửa. Chi tiết: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
