using Dfs.DataServer.Config;
using Dfs.DataServer.Hosting;
using Dfs.DataServer.Storage;

var config = DataServerConfig.FromEnvironment();

Console.WriteLine($"[DFS DataServer] NodeId: {config.NodeId}");
Console.WriteLine($"[DFS DataServer] ListenPort: {config.ListenPort}");
Console.WriteLine($"[DFS DataServer] AdvertiseAddress: {config.AdvertiseAddress}");
Console.WriteLine($"[DFS DataServer] DataDir: {config.DataDir}");
Console.WriteLine($"[DFS DataServer] MetadataEndpoints: {string.Join(", ", config.MetadataEndpoints)}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var storage = new ChunkStorage(config.DataDir, config.CapacityBytes);
await using var host = new DataServerHost(config, storage);
await using var heartbeat = new HeartbeatClient(config, storage);

await host.StartAsync(cts.Token).ConfigureAwait(false);
await heartbeat.StartAsync(cts.Token).ConfigureAwait(false);

Console.WriteLine($"[DFS DataServer] Listening on port {host.BoundPort}. Running until cancelled.");

try
{
    await Task.Delay(Timeout.Infinite, cts.Token).ConfigureAwait(false);
}
catch (OperationCanceledException)
{
    Console.WriteLine("[DFS DataServer] Shutting down...");
}

await heartbeat.StopAsync().ConfigureAwait(false);
await host.StopAsync().ConfigureAwait(false);
Console.WriteLine("[DFS DataServer] Stopped cleanly.");
