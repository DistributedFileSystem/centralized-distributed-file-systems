using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dfs.Shared;

namespace Dfs.Client.Desktop.ViewModels;

public partial class TransferManagerViewModel : ViewModelBase
{
    private readonly IDfsClient _client;

    [ObservableProperty]
    private ObservableCollection<TransferProgress> _transfers = [];

    [ObservableProperty]
    private string _filter = "All";

    [ObservableProperty]
    private string _summaryText = "Tổng tốc độ: 12.5 MB/s | Đang hoạt động: 2 tác vụ";

    public TransferManagerViewModel(IDfsClient client)
    {
        _client = client;
        _ = LoadTransfersAsync();
    }

    [RelayCommand]
    public async Task LoadTransfersAsync()
    {
        var list = await _client.GetTransfersAsync();
        Transfers.Clear();
        foreach (var t in list)
        {
            Transfers.Add(t);
        }
    }

    [RelayCommand]
    public async Task CancelTransferAsync(string? taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId)) return;
        await _client.CancelTransferAsync(taskId);
        await LoadTransfersAsync();
    }
}
