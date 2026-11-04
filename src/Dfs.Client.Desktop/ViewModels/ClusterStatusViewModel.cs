using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dfs.Shared;

namespace Dfs.Client.Desktop.ViewModels;

public partial class ClusterStatusViewModel : ViewModelBase
{
    private readonly IDfsClient _client;

    [ObservableProperty]
    private ObservableCollection<ClusterNodeInfo> _nodes = [];

    [ObservableProperty]
    private string _nodeSummary = "4 / 5 Trực tuyến";

    [ObservableProperty]
    private string _storageSummary = "380 GB trống / 500 GB";

    [ObservableProperty]
    private string _chunkSummary = "32,450 khối (1 bản sao / chunk)";

    [ObservableProperty]
    private string _metadataLeader = "Laptop 1 (Tân - 192.168.10.11:7000)";

    [ObservableProperty]
    private string _opLogSeq = "142";

    [ObservableProperty]
    private string _statusMessage = "Cập nhật nhịp tim thành công lúc " + DateTime.Now.ToString("HH:mm:ss");

    public ClusterStatusViewModel(IDfsClient client)
    {
        _client = client;
        _ = LoadClusterStatusAsync();
    }

    [RelayCommand]
    public async Task LoadClusterStatusAsync()
    {
        var info = await _client.GetClusterStatusAsync();
        Nodes = new ObservableCollection<ClusterNodeInfo>(info.Nodes);

        int healthy = info.Nodes.Count(n => n.IsHealthy);
        NodeSummary = $"{healthy} / {info.Nodes.Count} Trực tuyến";
        StorageSummary = $"{info.TotalFreeBytes / (1024.0 * 1024.0 * 1024.0):F0} GB trống / {info.TotalCapacityBytes / (1024.0 * 1024.0 * 1024.0):F0} GB";
        ChunkSummary = $"{info.TotalChunks:N0} khối (1 bản sao / chunk)";
        MetadataLeader = info.MetadataLeader;
        OpLogSeq = info.OpLogSeq.ToString();
        StatusMessage = $"Đã quét lại trạng thái 5 Data Server lúc {DateTime.Now:HH:mm:ss}";
    }
}
