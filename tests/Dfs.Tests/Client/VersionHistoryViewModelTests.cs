using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dfs.Client.Desktop.Models;
using Dfs.Client.Desktop.ViewModels;
using Dfs.Fakes;
using Dfs.Shared.Client;
using Dfs.Shared.Messages;
using Xunit;

namespace Dfs.Tests.Client;

public class VersionHistoryViewModelTests
{
    private readonly FakeDfsClient _client = new();

    [Fact]
    public void VersionHistoryViewModel_InitialState_PropertiesSetCorrectly()
    {
        var vm = new VersionHistoryViewModel(_client, "/projects/dfs/readme.txt", "readme.txt", () => { });

        Assert.Equal("/projects/dfs/readme.txt", vm.FilePath);
        Assert.Equal("readme.txt", vm.FileName);
        Assert.Contains("readme.txt", vm.Title);
        Assert.Contains("Write-Once", vm.Subtitle);
        Assert.False(vm.HasSelectedVersion);
        Assert.Empty(vm.Versions);
    }

    [Fact]
    public async Task VersionHistoryViewModel_LoadVersions_SortsNewestFirstAndSelectsCurrent()
    {
        await _client.LoginAsync("demo", "demo");
        var vm = new VersionHistoryViewModel(_client, "/projects/dfs/readme.txt", "readme.txt", () => { });

        await vm.LoadVersionsAsync();

        Assert.NotEmpty(vm.Versions);
        Assert.True(vm.HasSelectedVersion);

        // Versions must be sorted descending by version number
        for (int i = 0; i < vm.Versions.Count - 1; i++)
        {
            Assert.True(vm.Versions[i].Version > vm.Versions[i + 1].Version);
        }

        // The top version must be current
        Assert.True(vm.Versions[0].IsCurrent);
        Assert.Contains("(Mới nhất)", vm.Versions[0].VersionLabel);
        Assert.Equal(vm.Versions[0], vm.SelectedVersion);
    }

    [Fact]
    public async Task VersionHistoryViewModel_SelectVersion_UpdatesChunkVerificationDetails()
    {
        await _client.LoginAsync("demo", "demo");
        var vm = new VersionHistoryViewModel(_client, "/projects/dfs/readme.txt", "readme.txt", () => { });

        await vm.LoadVersionsAsync();
        Assert.True(vm.Versions.Count >= 2);

        var historical = vm.Versions[1]; // v2
        vm.SelectedVersion = historical;

        Assert.Equal($"v{historical.Version}", vm.SelectedVersionNumberText);
        Assert.NotEmpty(vm.SelectedVersionChunks);

        foreach (var chunk in vm.SelectedVersionChunks)
        {
            Assert.StartsWith("c-", chunk.ChunkId);
            Assert.False(string.IsNullOrWhiteSpace(chunk.Sha256));
            Assert.Equal(64, chunk.Sha256.Length); // 64 hex characters for SHA-256
        }
    }

    [Fact]
    public void VersionHistoryViewModel_DownloadVersion_InvokesCallbackWithSuggestedName()
    {
        string? downloadedPath = null;
        string? downloadedLocalName = null;
        long downloadedSize = 0;

        var vm = new VersionHistoryViewModel(
            _client,
            "/projects/dfs/report.pdf",
            "report.pdf",
            onClose: () => { },
            onDownloadVersion: (path, name, size) =>
            {
                downloadedPath = path;
                downloadedLocalName = name;
                downloadedSize = size;
            });

        var versionItem = new UiVersionItem(2, 5_000_000, "user.test", DateTime.UtcNow, 2, isCurrent: false);
        vm.ExecuteDownloadVersion(versionItem);

        Assert.Equal("/projects/dfs/report.pdf", downloadedPath);
        Assert.Equal("report_v2.pdf", downloadedLocalName);
        Assert.Equal(5_000_000, downloadedSize);
        Assert.True(vm.HasStatus);
        Assert.False(vm.StatusIsError);
    }

    [Fact]
    public void VersionHistoryViewModel_CloseCommand_InvokesCloseAction()
    {
        bool closed = false;
        var vm = new VersionHistoryViewModel(_client, "/test.txt", "test.txt", onClose: () => closed = true);

        vm.CloseCommand.Execute(null);

        Assert.True(closed);
    }

    [Fact]
    public async Task FileBrowserViewModel_VersionHistoryIntegration_OpensDialogForFile()
    {
        await _client.LoginAsync("demo", "demo");
        var fbVm = new FileBrowserViewModel(_client);

        var fileItem = new UiFileItem(new ItemInfo("i-1", "/test.txt", "test.txt", ItemKind.File, 100, "user", 1, DateTime.UtcNow));
        var folderItem = new UiFileItem(new ItemInfo("i-2", "/folder", "folder", ItemKind.Folder, 0, "user", 0, DateTime.UtcNow));

        // Folder cannot open version history
        fbVm.SelectedItem = folderItem;
        Assert.False(fbVm.CanOpenVersionHistory);

        // File can open version history
        fbVm.SelectedItem = fileItem;
        Assert.True(fbVm.CanOpenVersionHistory);

        fbVm.OpenVersionHistoryDialogCommand.Execute(fileItem);

        Assert.True(fbVm.IsVersionHistoryDialogOpen);
        Assert.NotNull(fbVm.VersionHistoryVm);
        Assert.Equal("/test.txt", fbVm.VersionHistoryVm.FilePath);

        fbVm.CloseVersionHistoryDialogCommand.Execute(null);
        Assert.False(fbVm.IsVersionHistoryDialogOpen);
    }
}
