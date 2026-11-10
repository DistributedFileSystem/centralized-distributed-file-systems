using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using Dfs.Client.Desktop.Models;
using Dfs.Shared.Client;

namespace Dfs.Client.Desktop.ViewModels;

public sealed class AdminPanelViewModel : ViewModelBase
{
    private readonly IDfsClient _client;
    private int _selectedTabIndex;
    private string _statusMessage = "";

    public ObservableCollection<UiUserItem> Users { get; } = [];
    public ObservableCollection<UiGroupItem> Groups { get; } = [];
    public ObservableCollection<UiGrantItem> Grants { get; } = [];

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ICommand RefreshCommand { get; }

    public AdminPanelViewModel(IDfsClient client)
    {
        _client = client;
        RefreshCommand = new RelayCommand(async () => await LoadDataAsync());
        LoadFallbackSamples();
    }

    public async Task LoadDataAsync()
    {
        StatusMessage = "Đang đồng bộ dữ liệu quản trị...";
        try
        {
            var users = await _client.ListUsersAsync();
            Users.Clear();
            foreach (var u in users) Users.Add(new UiUserItem(u));
        }
        catch (NotSupportedException)
        {
            // Fake client throws NotSupportedException - keep fallback samples
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi tải user: {ex.Message}";
        }

        try
        {
            var groups = await _client.ListGroupsAsync();
            Groups.Clear();
            foreach (var g in groups) Groups.Add(new UiGroupItem(g));
        }
        catch (NotSupportedException)
        {
            // Keep fallback samples
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi tải group: {ex.Message}";
        }

        try
        {
            var grants = await _client.ListGrantsAsync();
            Grants.Clear();
            foreach (var g in grants) Grants.Add(new UiGrantItem(g));
        }
        catch (NotSupportedException)
        {
            // Keep fallback samples
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lỗi tải grants: {ex.Message}";
        }

        StatusMessage = "Dữ liệu quản trị đã sẵn sàng.";
    }

    private void LoadFallbackSamples()
    {
        Users.Clear();
        Users.Add(new UiUserItem("u-admin", "admin", false, "admin-group"));
        Users.Add(new UiUserItem("u-demo", "demo", false, "developers, researchers"));
        Users.Add(new UiUserItem("u-guest", "guest", false, "guests"));
        Users.Add(new UiUserItem("u-locked", "attacker_test", true, "None"));

        Groups.Clear();
        Groups.Add(new UiGroupItem("g-admin", "admin-group", 1));
        Groups.Add(new UiGroupItem("g-dev", "developers", 4));
        Groups.Add(new UiGroupItem("g-research", "researchers", 2));

        Grants.Clear();
        Grants.Add(new UiGrantItem("gr-01", "Admin", "User: admin", "(Hệ thống)", true));
        Grants.Add(new UiGrantItem("gr-02", "Editor", "Group: developers", "/projects", true));
        Grants.Add(new UiGrantItem("gr-03", "Viewer", "Group: researchers", "/projects/reports", false));
    }
}

