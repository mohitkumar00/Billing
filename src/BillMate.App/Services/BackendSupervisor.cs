using System.Diagnostics;
using System.Net.Sockets;

namespace BillMate.App.Services;

public static class BackendSupervisor
{
    private static Process? _apiProcess;
    private static Process? _postgresProcess;
    private static readonly object _syncLock = new();

    public static async Task EnsureBackendRunningAsync()
    {
        try
        {
            // 1. Ensure Database (PostgreSQL) is running on port 5432
            await EnsurePostgresRunningAsync();

            // 2. Check if API is already healthy on port 5050
            if (await IsBackendHealthyAsync())
                return;

            lock (_syncLock)
            {
                if (_apiProcess != null && !_apiProcess.HasExited)
                    return;

                var baseDir = AppContext.BaseDirectory;

                var candidates = new[]
                {
                    Path.Combine(baseDir, "api", "BillMate.Api.exe"),
                    Path.Combine(baseDir, "BillMate.Api.exe"),
                    Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\..\src\BillMate.Api\bin\Debug\net10.0\BillMate.Api.exe")),
                    Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\..\src\BillMate.Api\bin\Release\net10.0\BillMate.Api.exe"))
                };

                var apiExe = candidates.FirstOrDefault(File.Exists);

                if (apiExe != null)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = apiExe,
                        WorkingDirectory = Path.GetDirectoryName(apiExe)!,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    psi.EnvironmentVariables["ASPNETCORE_URLS"] = "http://localhost:5050";
                    _apiProcess = Process.Start(psi);
                }
                else
                {
                    var devProject = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\..\src\BillMate.Api\BillMate.Api.csproj"));
                    if (File.Exists(devProject))
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "dotnet",
                            Arguments = $"run --project \"{devProject}\" --launch-profile http",
                            WorkingDirectory = Path.GetDirectoryName(devProject)!,
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        _apiProcess = Process.Start(psi);
                    }
                }
            }

            // Wait up to 15 seconds for backend to become healthy
            for (var i = 0; i < 30; i++)
            {
                await Task.Delay(500);
                if (await IsBackendHealthyAsync())
                    break;
            }
        }
        catch
        {
            // Ignore supervisor errors to keep UI running
        }
    }

    public static async Task EnsurePostgresRunningAsync()
    {
        if (await IsPortOpenAsync("127.0.0.1", 5432))
            return;

        lock (_syncLock)
        {
            if (_postgresProcess != null && !_postgresProcess.HasExited)
                return;

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var baseDir = AppContext.BaseDirectory;

            var postgresExeCandidates = new[]
            {
                Path.Combine(localAppData, "PostgreSQL", "pgsql", "bin", "postgres.exe"),
                Path.Combine(baseDir, "pgsql", "bin", "postgres.exe"),
                Path.Combine(baseDir, "db", "pgsql", "bin", "postgres.exe"),
                @"C:\Program Files\PostgreSQL\16\bin\postgres.exe"
            };

            var dataDirCandidates = new[]
            {
                Path.Combine(localAppData, "PostgreSQL", "data"),
                Path.Combine(baseDir, "data"),
                Path.Combine(baseDir, "db", "data")
            };

            var pgExe = postgresExeCandidates.FirstOrDefault(File.Exists);
            var dataDir = dataDirCandidates.FirstOrDefault(Directory.Exists);

            if (pgExe != null && dataDir != null)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = pgExe,
                        Arguments = $"-D \"{dataDir}\"",
                        WorkingDirectory = Path.GetDirectoryName(pgExe)!,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    _postgresProcess = Process.Start(psi);
                }
                catch { }
            }
        }

        // Wait up to 8 seconds for port 5432 to accept connections
        for (var i = 0; i < 16; i++)
        {
            await Task.Delay(500);
            if (await IsPortOpenAsync("127.0.0.1", 5432))
                break;
        }
    }

    public static async Task<bool> IsPortOpenAsync(string host, int port)
    {
        try
        {
            using var tcp = new TcpClient();
            var connectTask = tcp.ConnectAsync(host, port);
            var delayTask = Task.Delay(500);
            var completed = await Task.WhenAny(connectTask, delayTask);
            return completed == connectTask && tcp.Connected;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> IsBackendHealthyAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(1000) };
            var res = await client.GetAsync("http://localhost:5050/health/ping");
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static void Shutdown()
    {
        try
        {
            if (_apiProcess != null && !_apiProcess.HasExited)
            {
                _apiProcess.Kill(entireProcessTree: true);
                _apiProcess.Dispose();
                _apiProcess = null;
            }

            if (_postgresProcess != null && !_postgresProcess.HasExited)
            {
                _postgresProcess.Kill(entireProcessTree: true);
                _postgresProcess.Dispose();
                _postgresProcess = null;
            }
        }
        catch
        {
            // Ignore process shutdown errors
        }
    }
}
