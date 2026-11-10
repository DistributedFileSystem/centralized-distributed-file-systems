using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Dfs.Client.Desktop.Models;
using Dfs.Shared.Client;
using Dfs.Shared.Messages;

namespace Dfs.Client.Desktop.ViewModels;

public sealed class TransferManagerViewModel : ViewModelBase
{
    private readonly IDfsClient? _client;
    private string _filter = "all";
    private string _unavailableAlert = "";
    private string _newUploadPath = "/projects/dfs";
    private string _newUploadFileName = "data_analysis_report.pdf";
    private long _newUploadSizeBytes = 12 * 1024 * 1024; // 12 MB
    private bool _isUploadDialogOpen;

    public ObservableCollection<UiTransferItem> Transfers { get; } = [];

    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
            {
                OnPropertyChanged(nameof(FilteredTransfers));
                OnPropertyChanged(nameof(IsAllFilter));
                OnPropertyChanged(nameof(IsActiveFilter));
                OnPropertyChanged(nameof(IsCompletedFilter));
            }
        }
    }

    public bool IsAllFilter => _filter == "all";
    public bool IsActiveFilter => _filter == "active";
    public bool IsCompletedFilter => _filter == "completed";

    public string AllFilterHeader => $"Tất cả ({Transfers.Count})";
    public string ActiveFilterHeader => $"Đang truyền ({Transfers.Count(t => t.IsActive)})";
    public string CompletedFilterHeader => $"Hoàn tất ({Transfers.Count(t => t.IsCompleted)})";
    public string ActiveCountText => Transfers.Count(t => t.IsActive).ToString();
    public string TotalSpeedText => Transfers.Any(t => t.IsActive) ? "12.4 MB/s" : "0 MB/s";

    public IEnumerable<UiTransferItem> FilteredTransfers => _filter switch
    {
        "active" => Transfers.Where(t => t.IsActive),
        "completed" => Transfers.Where(t => t.IsCompleted),
        _ => Transfers
    };

    public string UnavailableAlert
    {
        get => _unavailableAlert;
        set
        {
            if (SetProperty(ref _unavailableAlert, value))
            {
                OnPropertyChanged(nameof(HasUnavailableAlert));
            }
        }
    }

    public bool HasUnavailableAlert => !string.IsNullOrEmpty(_unavailableAlert);

    public bool IsUploadDialogOpen
    {
        get => _isUploadDialogOpen;
        set => SetProperty(ref _isUploadDialogOpen, value);
    }

    public string NewUploadPath
    {
        get => _newUploadPath;
        set => SetProperty(ref _newUploadPath, value);
    }

    public string NewUploadFileName
    {
        get => _newUploadFileName;
        set => SetProperty(ref _newUploadFileName, value);
    }

    public ICommand ClearCompletedCommand { get; }
    public ICommand CancelTransferCommand { get; }
    public ICommand RetryTransferCommand { get; }
    public ICommand OpenUploadDialogCommand { get; }
    public ICommand ConfirmUploadCommand { get; }
    public ICommand CancelUploadDialogCommand { get; }
    public ICommand SimulateUnavailableCommand { get; }
    public ICommand DismissAlertCommand { get; }
    public ICommand SetFilterCommand { get; }

    public TransferManagerViewModel(IDfsClient? client = null)
    {
        _client = client;

        ClearCompletedCommand = new RelayCommand(ClearCompleted);
        CancelTransferCommand = new RelayCommand(param =>
        {
            if (param is UiTransferItem item)
            {
                item.Cancel();
                OnPropertyChanged(nameof(FilteredTransfers));
            }
        });

        RetryTransferCommand = new RelayCommand(param =>
        {
            if (param is UiTransferItem item)
            {
                _ = StartTransferAsync(item, simulateFailure: false);
            }
        });

        OpenUploadDialogCommand = new RelayCommand(() => IsUploadDialogOpen = true);
        CancelUploadDialogCommand = new RelayCommand(() => IsUploadDialogOpen = false);
        ConfirmUploadCommand = new RelayCommand(() =>
        {
            IsUploadDialogOpen = false;
            QueueNewUpload(NewUploadFileName, NewUploadPath, _newUploadSizeBytes);
        });

        SimulateUnavailableCommand = new RelayCommand(SimulateUnavailableScenario);
        DismissAlertCommand = new RelayCommand(() => UnavailableAlert = "");
        SetFilterCommand = new RelayCommand(param =>
        {
            if (param is string f) Filter = f;
        });

        SeedSampleTransfers();
    }

    private void SeedSampleTransfers()
    {
        // Add one completed upload
        var completed = new UiTransferItem
        {
            FileName = "presentation_slides_v2.pptx",
            DestinationPath = "/projects/dfs/presentation_slides_v2.pptx",
            Direction = "Upload",
            SizeText = "8.4 MB (3 chunks)",
            Progress = 100,
            Status = "Hoàn tất (Mã băm SHA-256 đã xác minh)",
            Speed = "12.4 MB/s",
            TargetServers = "DS-1 (4MB), DS-2 (4MB), DS-3 (0.4MB)",
            IsActive = false,
            IsCompleted = true
        };
        completed.Chunks.Add(new UiChunkItem { ChunkIndex = 0, ChunkSize = "4.0 MB", DataServer = "DS-1 (192.168.10.11:7001)", Progress = 100, Status = "100% (SHA-256 Verified)" });
        completed.Chunks.Add(new UiChunkItem { ChunkIndex = 1, ChunkSize = "4.0 MB", DataServer = "DS-2 (192.168.10.12:7001)", Progress = 100, Status = "100% (SHA-256 Verified)" });
        completed.Chunks.Add(new UiChunkItem { ChunkIndex = 2, ChunkSize = "0.4 MB", DataServer = "DS-3 (192.168.10.13:7001)", Progress = 100, Status = "100% (SHA-256 Verified)" });
        Transfers.Add(completed);

        // Queue active upload
        QueueNewUpload("report_distributed_storage.pdf", "/projects/dfs", 10 * 1024 * 1024);
    }

    public void QueueNewUpload(string fileName, string destPath, long sizeBytes)
    {
        var chunkCount = Math.Max(1, (int)Math.Ceiling(sizeBytes / (4.0 * 1024 * 1024)));
        var targetFile = destPath.EndsWith('/') ? $"{destPath}{fileName}" : $"{destPath}/{fileName}";
        var sizeMB = sizeBytes / (1024.0 * 1024.0);

        var item = new UiTransferItem
        {
            FileName = fileName,
            DestinationPath = targetFile,
            Direction = "Upload",
            SizeText = $"{sizeMB:F1} MB ({chunkCount} chunks)",
            Progress = 0,
            Status = "Đang khởi tạo các kết nối socket TCP...",
            Speed = "4.5 MB/s",
            TargetServers = "DS-1, DS-2, DS-4 (Phân mảnh LAN)",
            IsActive = true
        };

        for (int i = 0; i < chunkCount; i++)
        {
            item.Chunks.Add(new UiChunkItem
            {
                ChunkIndex = i,
                ChunkSize = i == chunkCount - 1 ? $"{sizeMB - (i * 4):F1} MB" : "4.0 MB",
                DataServer = $"DS-{(i % 5) + 1} (192.168.10.{11 + (i % 5)}:7001)",
                Progress = 0,
                Status = "Đang chờ gửi..."
            });
        }

        Transfers.Insert(0, item);
        OnPropertyChanged(nameof(FilteredTransfers));
        _ = StartTransferAsync(item, simulateFailure: false);
    }

    public void QueueNewDownload(string dfsPath, string localFileName, long sizeBytes)
    {
        var chunkCount = Math.Max(1, (int)Math.Ceiling(sizeBytes / (4.0 * 1024 * 1024)));
        var sizeMB = sizeBytes / (1024.0 * 1024.0);

        var item = new UiTransferItem
        {
            FileName = localFileName,
            DestinationPath = dfsPath,
            Direction = "Download",
            SizeText = $"{sizeMB:F1} MB ({chunkCount} chunks)",
            Progress = 0,
            Status = "Đang đọc metadata từ máy chủ...",
            Speed = "8.2 MB/s",
            TargetServers = "5 Data Servers (Đọc song song)",
            IsActive = true
        };

        for (int i = 0; i < chunkCount; i++)
        {
            item.Chunks.Add(new UiChunkItem
            {
                ChunkIndex = i,
                ChunkSize = "4.0 MB",
                DataServer = $"DS-{(i % 5) + 1} (192.168.10.{11 + (i % 5)}:7001)",
                Progress = 0,
                Status = "Đang đọc..."
            });
        }

        Transfers.Insert(0, item);
        OnPropertyChanged(nameof(FilteredTransfers));
        _ = StartTransferAsync(item, simulateFailure: false);
    }

    private void SimulateUnavailableScenario()
    {
        UnavailableAlert = "⚠️ Data Server 3 (192.168.10.13:7001) mất kết nối mạng LAN (Unavailable). Thao tác truyền tải bị ngắt.";

        var failedItem = new UiTransferItem
        {
            FileName = "large_dataset_offline_test.bin",
            DestinationPath = "/shared/large_dataset_offline_test.bin",
            Direction = "Upload",
            SizeText = "16.0 MB (4 chunks)",
            Progress = 35,
            Status = "Lỗi: Data Server không khả dụng (Unavailable)",
            Speed = "0 MB/s",
            TargetServers = "DS-3 (Dead / Timeout)",
            IsActive = false,
            IsFailed = true
        };

        failedItem.Chunks.Add(new UiChunkItem { ChunkIndex = 0, ChunkSize = "4.0 MB", DataServer = "DS-1", Progress = 100, Status = "100% OK" });
        failedItem.Chunks.Add(new UiChunkItem { ChunkIndex = 1, ChunkSize = "4.0 MB", DataServer = "DS-3 (192.168.10.13:7001)", Progress = 25, Status = "Mất kết nối (Unavailable)" });

        Transfers.Insert(0, failedItem);
        OnPropertyChanged(nameof(FilteredTransfers));
    }

    private async Task StartTransferAsync(UiTransferItem item, bool simulateFailure)
    {
        item.Cts?.Dispose();
        item.Cts = new CancellationTokenSource();
        var ct = item.Cts.Token;

        item.IsActive = true;
        item.IsFailed = false;
        item.IsCancelled = false;
        item.IsCompleted = false;
        item.Progress = 0;
        item.Status = "Đang kết nối socket và truyền các khối...";

        try
        {
            // Simulate chunk by chunk transfer with cancellation checking
            for (int p = 5; p <= 100; p += 5)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(150, ct);

                item.Progress = p;
                item.Speed = $"{(4.0 + (p % 3) * 1.5):F1} MB/s";
                item.Status = $"Đang truyền các khối dữ liệu ({p}%)...";

                // Update chunks status
                if (item.Chunks.Count > 0)
                {
                    var chunkIndex = Math.Min(item.Chunks.Count - 1, (p * item.Chunks.Count) / 100);
                    for (int c = 0; c < item.Chunks.Count; c++)
                    {
                        if (c < chunkIndex)
                        {
                            item.Chunks[c].Progress = 100;
                            item.Chunks[c].Status = "100% (SHA-256 Verified)";
                        }
                        else if (c == chunkIndex)
                        {
                            var chunkProgress = ((p * item.Chunks.Count) % 100);
                            item.Chunks[c].Progress = Math.Min(100, chunkProgress + 10);
                            item.Chunks[c].Status = $"{item.Chunks[c].Progress}% (Đang truyền)";
                        }
                    }
                }

                if (simulateFailure && p >= 40)
                {
                    throw new DfsException(ErrorCode.Unavailable, "Data Server 3 connection timed out (Unavailable).");
                }
            }

            item.Progress = 100;
            item.Status = "Hoàn tất (Mã băm SHA-256 đã xác minh)";
            item.Speed = "0 MB/s";
            item.IsActive = false;
            item.IsCompleted = true;

            foreach (var ch in item.Chunks)
            {
                ch.Progress = 100;
                ch.Status = "100% (SHA-256 Verified)";
            }
        }
        catch (OperationCanceledException)
        {
            item.IsActive = false;
            item.IsCancelled = true;
            item.Status = "Đã hủy bởi người dùng";
            item.Speed = "0 MB/s";
        }
        catch (DfsException ex) when (ex.Code == ErrorCode.Unavailable)
        {
            item.IsActive = false;
            item.IsFailed = true;
            item.Status = "Lỗi: Máy chủ dữ liệu không khả dụng (Unavailable)";
            item.Speed = "0 MB/s";
            UnavailableAlert = $"⚠️ Data Server ngắt kết nối: {ex.Message}";
        }
        catch (Exception ex)
        {
            item.IsActive = false;
            item.IsFailed = true;
            item.Status = $"Lỗi: {ex.Message}";
            item.Speed = "0 MB/s";
        }
        finally
        {
            OnPropertyChanged(nameof(FilteredTransfers));
        }
    }

    private void ClearCompleted()
    {
        for (int i = Transfers.Count - 1; i >= 0; i--)
        {
            if (Transfers[i].IsCompleted || Transfers[i].IsCancelled)
            {
                Transfers.RemoveAt(i);
            }
        }
        OnPropertyChanged(nameof(FilteredTransfers));
    }
}
