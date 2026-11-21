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
        var targetDll = ResolveMetadataServerDll();

        _tempDataDir = Path.Combine(Path.GetTempPath(), "dfs-acc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDataDir);

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(targetDll) ?? AppContext.BaseDirectory
        };
        psi.ArgumentList.Add(targetDll);

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

    private static string? TryFindSolutionDirectory()
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

        return null;
    }

    private static string ResolveMetadataServerDll()
    {
        var candidates = new List<string>();

        // 1. Output directory of test runner (AppContext.BaseDirectory)
        var baseDirDll = Path.Combine(AppContext.BaseDirectory, "Dfs.MetadataServer.dll");
        candidates.Add(baseDirDll);
        if (File.Exists(baseDirDll))
        {
            return Path.GetFullPath(baseDirDll);
        }

        // 2. Source build output fallback
        var slnDir = TryFindSolutionDirectory();
        if (slnDir is not null)
        {
            var baseDir = new DirectoryInfo(AppContext.BaseDirectory);
            var tfm = !string.IsNullOrWhiteSpace(baseDir.Name) ? baseDir.Name : "net10.0";
            var config = baseDir.Parent?.Name;

            if (string.Equals(config, "Release", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(Path.Combine(slnDir, "src", "Dfs.MetadataServer", "bin", "Release", tfm, "Dfs.MetadataServer.dll"));
                candidates.Add(Path.Combine(slnDir, "src", "Dfs.MetadataServer", "bin", "Release", "net10.0", "Dfs.MetadataServer.dll"));
                candidates.Add(Path.Combine(slnDir, "src", "Dfs.MetadataServer", "bin", "Debug", tfm, "Dfs.MetadataServer.dll"));
                candidates.Add(Path.Combine(slnDir, "src", "Dfs.MetadataServer", "bin", "Debug", "net10.0", "Dfs.MetadataServer.dll"));
            }
            else
            {
                candidates.Add(Path.Combine(slnDir, "src", "Dfs.MetadataServer", "bin", "Debug", tfm, "Dfs.MetadataServer.dll"));
                candidates.Add(Path.Combine(slnDir, "src", "Dfs.MetadataServer", "bin", "Debug", "net10.0", "Dfs.MetadataServer.dll"));
                candidates.Add(Path.Combine(slnDir, "src", "Dfs.MetadataServer", "bin", "Release", tfm, "Dfs.MetadataServer.dll"));
                candidates.Add(Path.Combine(slnDir, "src", "Dfs.MetadataServer", "bin", "Release", "net10.0", "Dfs.MetadataServer.dll"));
            }

            foreach (var candidate in candidates.Distinct())
            {
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        throw new FileNotFoundException(
            $"Dfs.MetadataServer.dll was not found at any candidate path: [{string.Join(", ", candidates.Distinct())}]. Ensure solution or project is built first.");
    }
}
