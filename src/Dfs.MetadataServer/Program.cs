using Dfs.MetadataServer.Config;
using Dfs.MetadataServer.Hosting;

var config = MetadataServerConfig.FromEnvironment();

Console.WriteLine($"Starting DFS Metadata Server [{config.NodeId}]...");
Console.WriteLine($"  Role:            {config.NodeRole}");
Console.WriteLine($"  Listen Port:     {config.ListenPort}");
Console.WriteLine($"  Advertise Host:  {config.AdvertiseHost}");
Console.WriteLine($"  Data Directory:  {config.DataDir}");
Console.WriteLine($"  Database Path:   {config.DatabasePath}");
Console.WriteLine($"  OpLog Path:      {config.OpLogPath}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.WriteLine("Shutdown signal received, stopping Metadata Server...");
    cts.Cancel();
};

AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    if (!cts.IsCancellationRequested)
    {
        cts.Cancel();
    }
};

await using var host = new MetadataServerHost(config);
await host.StartAsync(cts.Token);
Console.WriteLine($"Metadata Server [{config.NodeId}] is listening on port {host.BoundPort}. Press Ctrl+C to terminate.");

try
{
    await host.WaitForShutdownAsync(cts.Token);
}
catch (OperationCanceledException)
{
    // Graceful cancellation
}

Console.WriteLine("DFS Metadata Server terminated cleanly.");
