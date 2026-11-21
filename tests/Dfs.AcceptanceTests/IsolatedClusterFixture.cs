using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Dfs.Client.Core;
using Dfs.Shared.Framing;
using Dfs.Shared.Messages;
using Xunit;

namespace Dfs.AcceptanceTests;

[CollectionDefinition("IsolatedCluster")]
public class IsolatedClusterCollection : ICollectionFixture<IsolatedClusterFixture>
{
}

/// <summary>
/// Manages a strictly isolated DFS cluster consisting of 1 Metadata Server and 2 Data Servers
/// running as separate out-of-process instances on dedicated ephemeral ports with isolated data directories.
/// Does not connect to or fall back to external shared clusters.
/// </summary>
public sealed class IsolatedClusterFixture : IAsyncLifetime
{
    private Process? _metaProcess;
    private Process? _dataProcess1;
    private Process? _dataProcess2;

    private string? _metaDir;
    private string? _dataDir1;
    private string? _dataDir2;

    private readonly List<string> _logs = new();

    public string Host { get; } = "127.0.0.1";
    public int MetadataPort { get; private set; }
    public int DataPort1 { get; private set; }
    public int DataPort2 { get; private set; }

    public string AdminUser { get; } = "admin";
    public string AdminPassword { get; } = "replace-with-local-dev-admin-password";
    public string TokenKey { get; } = "acceptance-tests-hmac-token-secret-32b!";

    public string MetadataEndpoint => $"{Host}:{MetadataPort}";

    public async Task InitializeAsync()
    {
        var slnDir = FindSolutionDirectory();
        var metaDll = ResolveDll(slnDir, "Dfs.MetadataServer");
        var dataDll = ResolveDll(slnDir, "Dfs.DataServer");

        // 1. Prepare isolated temporary directories
        var runId = Guid.NewGuid().ToString("N");
        _metaDir = Path.Combine(Path.GetTempPath(), $"dfs-iso-meta-{runId}");
        _dataDir1 = Path.Combine(Path.GetTempPath(), $"dfs-iso-ds1-{runId}");
        _dataDir2 = Path.Combine(Path.GetTempPath(), $"dfs-iso-ds2-{runId}");

        Directory.CreateDirectory(_metaDir);
        Directory.CreateDirectory(_dataDir1);
        Directory.CreateDirectory(_dataDir2);

        // 2. Start dedicated Metadata Server on ephemeral port (LISTEN_PORT=0)
        var metaPsi = new ProcessStartInfo("dotnet", $"\"{metaDll}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        metaPsi.EnvironmentVariables["NODE_ROLE"] = "metadata";
        metaPsi.EnvironmentVariables["NODE_ID"] = "meta-iso";
        metaPsi.EnvironmentVariables["LISTEN_PORT"] = "0";
        metaPsi.EnvironmentVariables["ADVERTISE_HOST"] = Host;
        metaPsi.EnvironmentVariables["DATA_DIR"] = _metaDir;
        metaPsi.EnvironmentVariables["ADMIN_USER"] = AdminUser;
        metaPsi.EnvironmentVariables["ADMIN_PASSWORD"] = AdminPassword;
        metaPsi.EnvironmentVariables["TOKEN_KEY"] = TokenKey;

        _metaProcess = new Process { StartInfo = metaPsi };

        var metaPortTcs = new TaskCompletionSource<int>();
        var metaPortRegex = new Regex(@"is listening on port (\d+)", RegexOptions.Compiled);

        _metaProcess.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (_logs) _logs.Add($"[META OUT] {e.Data}");
            var m = metaPortRegex.Match(e.Data);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var port))
            {
                metaPortTcs.TrySetResult(port);
            }
        };
        _metaProcess.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (_logs) _logs.Add($"[META ERR] {e.Data}");
        };

        _metaProcess.Start();
        _metaProcess.BeginOutputReadLine();
        _metaProcess.BeginErrorReadLine();

        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        {
            cts.Token.Register(() => metaPortTcs.TrySetCanceled());
            MetadataPort = await metaPortTcs.Task.ConfigureAwait(false);
        }

        // Wait for Metadata socket to be reachable
        await WaitForSocketAsync(Host, MetadataPort, TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        // 3. Allocate free ports for 2 Data Servers
        var (p1, p2) = GetTwoFreePorts();
        DataPort1 = p1;
        DataPort2 = p2;

        // 4. Start Data Server 1
        _dataProcess1 = StartDataServerProcess(dataDll, "ds-iso-1", DataPort1, _dataDir1);

        // 5. Start Data Server 2
        _dataProcess2 = StartDataServerProcess(dataDll, "ds-iso-2", DataPort2, _dataDir2);

        // 6. Verify Data Servers become Alive via Metadata Server ClusterStatus
        await WaitForClusterAliveAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
    }

    private Process StartDataServerProcess(string dataDll, string nodeId, int listenPort, string dataDir)
    {
        var psi = new ProcessStartInfo("dotnet", $"\"{dataDll}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.EnvironmentVariables["NODE_ROLE"] = "data";
        psi.EnvironmentVariables["NODE_ID"] = nodeId;
        psi.EnvironmentVariables["LISTEN_PORT"] = listenPort.ToString();
        psi.EnvironmentVariables["ADVERTISE_HOST"] = Host;
        psi.EnvironmentVariables["METADATA_ENDPOINTS"] = $"{Host}:{MetadataPort}";
        psi.EnvironmentVariables["DATA_DIR"] = dataDir;
        psi.EnvironmentVariables["TOKEN_KEY"] = TokenKey;
        psi.EnvironmentVariables["CAPACITY_BYTES"] = (50L * 1024 * 1024 * 1024).ToString(); // 50 GB

        var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (_logs) _logs.Add($"[{nodeId} OUT] {e.Data}");
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (_logs) _logs.Add($"[{nodeId} ERR] {e.Data}");
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        return proc;
    }

    private async Task WaitForClusterAliveAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await using var client = await RawTcpTestClient.ConnectAsync(Host, MetadataPort).ConfigureAwait(false);
                var helloRes = await client.HandshakeAsync().ConfigureAwait(false);
                if (helloRes.ProtocolVersion == 1)
                {
                    var loginReq = new Login(Guid.NewGuid().ToString("D"), AdminUser, AdminPassword);
                    await client.SendMessageAsync(MessageType.Login, loginReq).ConfigureAwait(false);

                    var loginRes = await client.ReceiveMessageAsync<LoginResponse>(MessageType.LoginResponse).ConfigureAwait(false);
                    if (loginRes is not null && !string.IsNullOrWhiteSpace(loginRes.Session))
                    {
                        var statusReq = new ClusterStatus(Guid.NewGuid().ToString("D"), loginRes.Session);
                        await client.SendMessageAsync(MessageType.ClusterStatus, statusReq).ConfigureAwait(false);

                        var statusRes = await client.ReceiveMessageAsync<ClusterStatusResponse>(MessageType.ClusterStatusResponse).ConfigureAwait(false);
                        if (statusRes is not null)
                        {
                            var aliveCount = statusRes.DataServers.Count(s => s.Status == DataServerStatus.Alive);
                            if (aliveCount >= 2)
                            {
                                return; // Both data servers registered and healthy
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                lock (_logs) _logs.Add($"[PROBE ERR] {ex.Message}");
            }

            await Task.Delay(200).ConfigureAwait(false);
        }

        string logDump;
        lock (_logs) logDump = string.Join(Environment.NewLine, _logs);

        throw new InvalidOperationException(
            $"Isolated cluster failed to initialize: Data servers failed to register as Alive within {timeout.TotalSeconds} seconds.{Environment.NewLine}Logs:{Environment.NewLine}{logDump}");
    }

    private static async Task WaitForSocketAsync(string host, int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var tcp = new TcpClient();
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                await tcp.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
                if (tcp.Connected) return;
            }
            catch
            {
                // Continue waiting
            }
            await Task.Delay(100).ConfigureAwait(false);
        }

        throw new TimeoutException($"Could not connect to {host}:{port} within {timeout.TotalSeconds} seconds.");
    }

    /// <summary>
    /// Creates an authenticated DfsClient connected to the isolated cluster as Admin.
    /// </summary>
    public async Task<DfsClient> CreateAdminClientAsync(CancellationToken cancellationToken = default)
    {
        var options = new DfsClientOptions
        {
            MetadataEndpoints = [MetadataEndpoint],
            RequestTimeout = TimeSpan.FromSeconds(15)
        };

        var client = new DfsClient(options);
        await client.LoginAsync(AdminUser, AdminPassword, cancellationToken).ConfigureAwait(false);
        return client;
    }

    public async Task DisposeAsync()
    {
        void KillProcess(ref Process? proc)
        {
            if (proc is null) return;
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(3000);
                }
            }
            catch
            {
            }
            finally
            {
                proc.Dispose();
                proc = null;
            }
        }

        KillProcess(ref _dataProcess1);
        KillProcess(ref _dataProcess2);
        KillProcess(ref _metaProcess);

        void DeleteDir(string? dir)
        {
            if (dir is null || !Directory.Exists(dir)) return;
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
            }
        }

        DeleteDir(_dataDir1);
        DeleteDir(_dataDir2);
        DeleteDir(_metaDir);

        await Task.CompletedTask;
    }

    private static (int Port1, int Port2) GetTwoFreePorts()
    {
        using var l1 = new TcpListener(IPAddress.Loopback, 0);
        l1.Start();
        var p1 = ((IPEndPoint)l1.LocalEndpoint).Port;

        using var l2 = new TcpListener(IPAddress.Loopback, 0);
        l2.Start();
        var p2 = ((IPEndPoint)l2.LocalEndpoint).Port;

        l1.Stop();
        l2.Stop();
        return (p1, p2);
    }

    private static string FindSolutionDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Dfs.sln")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }

        throw new InvalidOperationException($"Could not locate Dfs.sln from {AppContext.BaseDirectory}.");
    }

    private static string ResolveDll(string slnDir, string projectName)
    {
        var debugDll = Path.Combine(slnDir, "src", projectName, "bin", "Debug", "net10.0", $"{projectName}.dll");
        var releaseDll = Path.Combine(slnDir, "src", projectName, "bin", "Release", "net10.0", $"{projectName}.dll");

        if (File.Exists(releaseDll)) return releaseDll;
        if (File.Exists(debugDll)) return debugDll;

        throw new FileNotFoundException($"{projectName}.dll was not found at '{debugDll}' or '{releaseDll}'. Ensure solution is built first.");
    }
}
