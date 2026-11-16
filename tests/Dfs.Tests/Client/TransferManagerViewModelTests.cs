using System;
using System.Linq;
using System.Threading.Tasks;
using Dfs.Client.Desktop.Models;
using Dfs.Client.Desktop.ViewModels;
using Dfs.Fakes;
using Xunit;

namespace Dfs.Tests.Client;

public class TransferManagerViewModelTests
{
    private readonly FakeDfsClient _fakeClient = new();

    [Fact]
    public void Constructor_InitializesEmptyTransfers()
    {
        var vm = new TransferManagerViewModel(_fakeClient);

        Assert.NotNull(vm.Transfers);
        Assert.Equal("all", vm.Filter);
        Assert.True(vm.IsAllFilter);
        Assert.False(vm.HasUnavailableAlert);
    }

    [Fact]
    public void QueueNewUpload_AddsActiveTransfer()
    {
        var vm = new TransferManagerViewModel(_fakeClient);

        vm.QueueNewUpload("report.pdf", "/projects/dfs", 8 * 1024 * 1024);

        Assert.Single(vm.Transfers);
        var item = vm.Transfers[0];
        Assert.Equal("report.pdf", item.FileName);
        Assert.Equal("/projects/dfs/report.pdf", item.DestinationPath);
        Assert.Equal("Upload", item.Direction);
        Assert.Equal(2, item.Chunks.Count); // 8MB = 2 chunks of 4MB
        Assert.True(item.CanCancel);
    }

    [Fact]
    public void CancelTransfer_SetsCancelledState()
    {
        var vm = new TransferManagerViewModel(_fakeClient);
        vm.QueueNewUpload("large_dataset.bin", "/projects", 16 * 1024 * 1024);
        var item = vm.Transfers[0];

        vm.CancelTransferCommand.Execute(item);

        Assert.False(item.IsActive);
        Assert.True(item.IsCancelled);
        Assert.False(item.CanCancel);
        Assert.True(item.CanRetry);
        Assert.Contains("hủy", item.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Filter_SeparatesActiveAndCompleted()
    {
        var vm = new TransferManagerViewModel(_fakeClient);
        vm.QueueNewUpload("file1.txt", "/projects", 1024);
        vm.QueueNewUpload("file2.txt", "/projects", 1024);

        // Note: QueueNewUpload inserts at index 0, so Transfers[0] is file2.txt, Transfers[1] is file1.txt
        var item2 = vm.Transfers.First(t => t.FileName == "file2.txt");
        item2.Cancel(); // item2 cancelled

        var item1 = vm.Transfers.First(t => t.FileName == "file1.txt");
        item1.IsActive = false;
        item1.IsCompleted = true;

        vm.Filter = "active";
        Assert.Empty(vm.FilteredTransfers);

        vm.Filter = "completed";
        Assert.Single(vm.FilteredTransfers);
        Assert.Equal("file1.txt", vm.FilteredTransfers.First().FileName);

        vm.Filter = "all";
        Assert.Equal(2, vm.FilteredTransfers.Count());
    }

    [Fact]
    public void ClearCompleted_RemovesOnlyCompletedItems()
    {
        var vm = new TransferManagerViewModel(_fakeClient);
        vm.QueueNewUpload("active.zip", "/projects", 1024);
        vm.QueueNewUpload("completed.zip", "/projects", 1024);

        var completedItem = vm.Transfers.First(t => t.FileName == "completed.zip");
        completedItem.IsActive = false;
        completedItem.IsCompleted = true;

        vm.ClearCompletedCommand.Execute(null);

        Assert.Single(vm.Transfers);
        Assert.Equal("active.zip", vm.Transfers[0].FileName);
    }
}
