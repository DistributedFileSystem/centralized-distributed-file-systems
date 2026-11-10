using System.Collections.ObjectModel;
using System.Windows.Input;
using Dfs.Client.Desktop.Models;

namespace Dfs.Client.Desktop.ViewModels;

public sealed class TransferManagerViewModel : ViewModelBase
{
    public ObservableCollection<UiTransferItem> Transfers { get; } = [];

    public ICommand ClearCompletedCommand { get; }

    public TransferManagerViewModel()
    {
        ClearCompletedCommand = new RelayCommand(ClearCompleted);
        LoadSampleTransfers();
    }

    private void LoadSampleTransfers()
    {
        Transfers.Add(new UiTransferItem
        {
            FileName = "thesis_final_v1.pdf (12.4 MB)",
            Direction = "Upload",
            Progress = 100,
            Status = "Hoàn tất",
            TargetServers = "DS-1 (4MB), DS-2 (4MB), DS-3 (4.4MB)",
            Speed = "11.2 MB/s"
        });

        Transfers.Add(new UiTransferItem
        {
            FileName = "dataset_chunk_large.bin (45.0 MB)",
            Direction = "Download",
            Progress = 65,
            Status = "Đang tải (Chunk 8/12)",
            TargetServers = "DS-2, DS-4, DS-5 (LAN Parallel)",
            Speed = "18.5 MB/s"
        });

        Transfers.Add(new UiTransferItem
        {
            FileName = "backup_metadata.sqlite (2.1 MB)",
            Direction = "Upload",
            Progress = 40,
            Status = "Đang tải lên (Chunk 1/1)",
            TargetServers = "DS-1 (127.0.0.1:7001)",
            Speed = "5.0 MB/s"
        });
    }

    private void ClearCompleted()
    {
        for (int i = Transfers.Count - 1; i >= 0; i--)
        {
            if (Transfers[i].Progress == 100)
            {
                Transfers.RemoveAt(i);
            }
        }
    }
}

