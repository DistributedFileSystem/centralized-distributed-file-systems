using System;
using System.Threading.Tasks;
using Dfs.Client.Desktop.ViewModels;
using Dfs.Fakes;
using Dfs.Shared.Client;
using Dfs.Shared.Messages;
using Xunit;

namespace Dfs.Tests.Client;

public class LoginViewModelTests
{
    private readonly FakeDfsClient _fakeClient = new();

    [Fact]
    public void Constructor_DefaultValues_ArePopulated()
    {
        var vm = new LoginViewModel(_fakeClient, _ => { });

        Assert.False(string.IsNullOrEmpty(vm.Username));
        Assert.False(string.IsNullOrEmpty(vm.Password));
        Assert.Equal("127.0.0.1:7000", vm.Endpoint);
        Assert.False(vm.IsBusy);
        Assert.False(vm.HasError);
    }

    [Fact]
    public void ClearingFields_AllowsEmptyOrNull_AndRetainsRetypedValues()
    {
        var vm = new LoginViewModel(_fakeClient, _ => { });

        // Step 1: Clear username and password
        vm.Username = "";
        vm.Password = "";
        Assert.Equal("", vm.Username);
        Assert.Equal("", vm.Password);

        // Step 2: Clear to null (as Avalonia TextBox may emit on delete)
        vm.Username = null;
        vm.Password = null;
        Assert.Null(vm.Username);
        Assert.Null(vm.Password);

        // Step 3: Type new values
        vm.Username = "alice";
        vm.Password = "newsecret456";

        Assert.Equal("alice", vm.Username);
        Assert.Equal("newsecret456", vm.Password);
    }

    [Fact]
    public void PropertyChanged_FiresWhenFieldsChange()
    {
        var vm = new LoginViewModel(_fakeClient, _ => { });
        string? changedProperty = null;
        vm.PropertyChanged += (_, e) => changedProperty = e.PropertyName;

        vm.Username = "bob";
        Assert.Equal(nameof(vm.Username), changedProperty);

        vm.Password = "secret";
        Assert.Equal(nameof(vm.Password), changedProperty);

        vm.Endpoint = "192.168.10.11:7000";
        Assert.Equal(nameof(vm.Endpoint), changedProperty);
    }

    [Fact]
    public async Task Login_WhenUsernameCleared_ShowsValidationError()
    {
        var vm = new LoginViewModel(_fakeClient, _ => { });
        vm.Username = "";
        vm.Password = "demo";

        vm.LoginCommand.Execute(null);

        Assert.True(vm.HasError);
        Assert.Equal("Vui lòng nhập tên đăng nhập.", vm.ErrorMessage);
    }

    [Fact]
    public async Task Login_WhenPasswordCleared_ShowsValidationError()
    {
        var vm = new LoginViewModel(_fakeClient, _ => { });
        vm.Username = "demo";
        vm.Password = "";

        vm.LoginCommand.Execute(null);

        Assert.True(vm.HasError);
        Assert.Equal("Vui lòng nhập mật khẩu.", vm.ErrorMessage);
    }

    [Fact]
    public async Task Login_WhenRetypedWithValidCredentials_Succeeds()
    {
        LoginResponse? successResponse = null;
        var vm = new LoginViewModel(_fakeClient, (resp, _) => successResponse = resp);

        // Clear and retype credentials
        vm.Username = "";
        vm.Password = "";
        vm.Username = "demo";
        vm.Password = "demo";

        vm.LoginCommand.Execute(null);

        // Wait brief delay in ExecuteLoginAsync
        await Task.Delay(350);

        Assert.NotNull(successResponse);
        Assert.Equal("demo", successResponse.User.Username);
        Assert.False(vm.HasError);
    }
}
