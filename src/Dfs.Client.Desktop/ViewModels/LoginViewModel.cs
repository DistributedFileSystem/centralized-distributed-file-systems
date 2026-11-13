using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Dfs.Shared.Client;
using Dfs.Shared.Messages;

namespace Dfs.Client.Desktop.ViewModels;

public sealed class LoginViewModel : ViewModelBase
{
    private static readonly string StateFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CentralizedDfs",
        "login_state.json");

    private readonly IDfsClient _client;
    private readonly Action<LoginResponse, string> _onLoginSuccess;

    private string _username = "demo";
    private string _password = "demo";
    private string _endpoint = "127.0.0.1:7000";
    private bool _rememberMe = true;
    private bool _isPasswordRevealed;
    private string _errorMessage = "";
    private bool _isBusy;
    private int _selectedModeIndex = 0; // 0: Auto, 1: User Client, 2: Admin Panel

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

    public int SelectedModeIndex
    {
        get => _selectedModeIndex;
        set => SetProperty(ref _selectedModeIndex, value);
    }

    public IReadOnlyList<string> AvailableModes { get; } = new[]
    {
        "🔄 Tự động (Theo quyền tài khoản)",
        "📁 User Client (Giao diện Người dùng)",
        "🛡️ Admin Panel (Giao diện Quản trị viên)"
    };

    public ICommand LoginCommand { get; }
    public ICommand TogglePasswordRevealCommand { get; }
    public ICommand DismissErrorCommand { get; }

    public LoginViewModel(IDfsClient client, Action<LoginResponse, string> onLoginSuccess)
    {
        _client = client;
        _onLoginSuccess = onLoginSuccess;

        LoadSavedState();

        LoginCommand = new RelayCommand(async () =>
        {
            var mode = SelectedModeIndex switch
            {
                1 => "user",
                2 => "admin",
                _ => "auto"
            };
            await ExecuteLoginAsync(mode);
        });

        TogglePasswordRevealCommand = new RelayCommand(() => IsPasswordRevealed = !IsPasswordRevealed);
        DismissErrorCommand = new RelayCommand(() => ErrorMessage = "");
    }

    private void LoadSavedState()
    {
        try
        {
            if (File.Exists(StateFilePath))
            {
                var json = File.ReadAllText(StateFilePath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("rememberMe", out var rm) && rm.GetBoolean())
                {
                    _rememberMe = true;
                    if (doc.RootElement.TryGetProperty("username", out var u))
                        _username = u.GetString() ?? "demo";
                    if (doc.RootElement.TryGetProperty("endpoint", out var ep))
                        _endpoint = ep.GetString() ?? "127.0.0.1:7000";
                }
            }
        }
        catch
        {
            // Bỏ qua lỗi nạp file
        }
    }

    private void SaveLoginState()
    {
        try
        {
            var dir = Path.GetDirectoryName(StateFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (RememberMe)
            {
                var json = JsonSerializer.Serialize(new
                {
                    username = Username?.Trim() ?? "",
                    rememberMe = true,
                    endpoint = Endpoint?.Trim() ?? "127.0.0.1:7000"
                }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(StateFilePath, json);
            }
            else
            {
                if (File.Exists(StateFilePath))
                    File.Delete(StateFilePath);
            }
        }
        catch
        {
            // Bỏ qua lỗi ghi file
        }
    }

    private async Task ExecuteLoginAsync(string targetMode)
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
            // Độ trễ ngắn để tester dễ quan sát Loading indicator (28-LOGIN-08)
            await Task.Delay(250);

            var response = await _client.LoginAsync(Username.Trim(), Password);
            SaveLoginState();
            _onLoginSuccess(response, targetMode);
        }
        catch (DfsException ex)
        {
            ErrorMessage = ex.Code switch
            {
                ErrorCode.Unauthorized => "Tên đăng nhập hoặc mật khẩu không chính xác (Thử demo/demo).",
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
