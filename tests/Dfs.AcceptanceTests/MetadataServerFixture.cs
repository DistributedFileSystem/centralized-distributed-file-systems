using System.Diagnostics;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Xunit;

namespace Dfs.AcceptanceTests;

/// <summary>
/// Manages the lifecycle of a Metadata Server for black-box acceptance tests.
/// If an external Metadata Server is already running (e.g. in Docker Compose on port 7000),
/// the fixture uses it. Otherwise, it starts a dedicated out-of-process Metadata Server instance
/// on an ephemeral port with an isolated temporary data directory.
/// </summary>
public sealed class MetadataServerFixture : IAsyncLifetime
{
    private Process? _serverProcess;
    private string? _tempDataDir;

    public string Host { get; private set; } = "127.0.0.1";
    public int Port { get; private set; } = 7000;
    public string AdminUser { get; private set; } = "admin";
    public string AdminPassword { get; private set; } = "replace-with-local-dev-admin-password";

    public async Task InitializeAsync()
    {
        // 1. Check if an external server is already reachable (e.g., in CI or Docker Compose)
        var customEndpoint = Environment.GetEnvironmentVariable("METADATA_ENDPOINT")
                             ?? Environment.GetEnvironmentVariable("DFS_METADATA_ENDPOINT");

        if (!string.IsNullOrWhiteSpace(customEndpoint))
        {
            var parts = customEndpoint.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1], out var p))
            {
                Host = parts[0];
                Port = p;
            }
        }

        if (await CanConnectAsync(Host, Port))
        {
            return;
        }

        // 2. Start an isolated out-of-process Metadata Server
        var slnDir = FindSolutionDirectory();
        var targetDll = ResolveMetadataServerDll(slnDir);

        _tempDataDir = Path.Combine(Path.GetTempPath(), "dfs-acc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDataDir);

        var psi = new ProcessStartInfo("dotnet", $"\"{targetDll}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.EnvironmentVariables["NODE_ROLE"] = "metadata";
        psi.EnvironmentVariables["NODE_ID"] = "meta-acc";
        psi.EnvironmentVariables["LISTEN_PORT"] = "0"; // Dynamic ephemeral port
        psi.EnvironmentVariables["ADVERTISE_HOST"] = "127.0.0.1";
        psi.EnvironmentVariables["DATA_DIR"] = _tempDataDir;
        psi.EnvironmentVariables["ADMIN_USER"] = AdminUser;
        psi.EnvironmentVariables["ADMIN_PASSWORD"] = AdminPassword;
        psi.EnvironmentVariables["TOKEN_KEY"] = "acceptance-tests-hmac-token-secret-32b!";

        _serverProcess = new Process { StartInfo = psi };

        var tcs = new TaskCompletionSource<int>();
        var regex = new Regex(@"is listening on port (\d+)", RegexOptions.Compiled);

        _serverProcess.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            var match = regex.Match(e.Data);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var boundPort))
            {
                tcs.TrySetResult(boundPort);
            }
        };

        _serverProcess.Start();
        _serverProcess.BeginOutputReadLine();
        _serverProcess.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        cts.Token.Register(() => tcs.TrySetCanceled());

        Port = await tcs.Task;
        Host = "127.0.0.1";

        // Wait until TCP socket accepts connections
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (await CanConnectAsync(Host, Port))
            {
                return;
            }
            await Task.Delay(50);
        }

        throw new TimeoutException($"Metadata Server process started on port {Port} but socket was not reachable.");
    }

    public async Task DisposeAsync()
    {
        if (_serverProcess != null)
        {
            try
            {
                if (!_serverProcess.HasExited)
                {
                    _serverProcess.Kill(entireProcessTree: true);
                    await _serverProcess.WaitForExitAsync();
                }
            }
            catch
            {
                // Process may have already exited
            }
            finally
            {
                _serverProcess.Dispose();
                _serverProcess = null;
            }
        }

        if (_tempDataDir != null && Directory.Exists(_tempDataDir))
        {
            try
            {
                Directory.Delete(_tempDataDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    private static async Task<bool> CanConnectAsync(string host, int port)
    {
        try
        {
            using var tcp = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            await tcp.ConnectAsync(host, port, cts.Token);
            return tcp.Connected;
        }
        catch
        {
            return false;
        }
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

    private static string ResolveMetadataServerDll(string slnDir)
    {
        var debugDll = Path.Combine(slnDir, "src", "Dfs.MetadataServer", "bin", "Debug", "net10.0", "Dfs.MetadataServer.dll");
        var releaseDll = Path.Combine(slnDir, "src", "Dfs.MetadataServer", "bin", "Release", "net10.0", "Dfs.MetadataServer.dll");

        if (File.Exists(releaseDll)) return releaseDll;
        if (File.Exists(debugDll)) return debugDll;

        throw new FileNotFoundException($"Dfs.MetadataServer.dll was not found at '{debugDll}' or '{releaseDll}'. Ensure solution is built first.");
    }
}
