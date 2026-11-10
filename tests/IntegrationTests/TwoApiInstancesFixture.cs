using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// One real API server running as its own operating-system process (<c>dotnet Api.dll</c>), listening on its own TCP port and
/// talking to a real Postgres. Nothing is shared with the test process except the network: no memory, no connection pool,
/// no DI container, which is what makes it a fair stand-in for a second server in a deployment.
/// </summary>
public sealed class ApiProcess : IAsyncDisposable
{
    private const int TailLines = 60;

    private readonly Process _process;
    private readonly ConcurrentQueue<string> _tail = new();

    private ApiProcess(string name, int port, Process process)
    {
        Name = name;
        Port = port;
        _process = process;
        Client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    }

    public string Name { get; }
    public int Port { get; }
    public int Pid => _process.Id;
    public bool HasExited => _process.HasExited;
    public HttpClient Client { get; }

    /// <summary>The last lines the process wrote (Serilog console), for failure messages.</summary>
    public string OutputTail => string.Join(Environment.NewLine, _tail);

    public static async Task<ApiProcess> StartAsync(string name, string connectionString)
    {
        var dll = FindApiDll();
        var port = FreePort();

        var psi = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(dll)!, // content root: appsettings.json sits next to Api.dll
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(dll);

        // Exactly what the Docker deployment passes: the connection string and the listen address as environment variables.
        // HOLD_DURATION_SECONDS and the expiry-sweep variables are left alone, so this runs with the production defaults
        // (including the background sweep).
        psi.Environment["ConnectionStrings__Postgres"] = connectionString;
        psi.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        psi.Environment.Remove("ASPNETCORE_HTTP_PORTS");
        psi.Environment.Remove("ASPNETCORE_HTTPS_PORTS");

        var process = new Process { StartInfo = psi };
        var api = new ApiProcess(name, port, process);
        process.OutputDataReceived += (_, e) => api.Remember(e.Data);
        process.ErrorDataReceived += (_, e) => api.Remember(e.Data);

        try
        {
            if (!process.Start()) throw new InvalidOperationException($"API instance {name} did not start.");
            process.BeginOutputReadLine(); // always drained: a full pipe would stall the server
            process.BeginErrorReadLine();
            await api.WaitUntilHealthyAsync(TimeSpan.FromSeconds(90));
            return api;
        }
        catch
        {
            await api.DisposeAsync();
            throw;
        }
    }

    private void Remember(string? line)
    {
        if (line is null) return;
        _tail.Enqueue(line);
        while (_tail.Count > TailLines && _tail.TryDequeue(out _)) { }
    }

    private async Task WaitUntilHealthyAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        using var probe = new HttpClient { BaseAddress = Client.BaseAddress, Timeout = TimeSpan.FromSeconds(3) };
        while (true)
        {
            if (_process.HasExited)
                throw new InvalidOperationException(
                    $"API instance {Name} exited with code {_process.ExitCode} before it became healthy.{Environment.NewLine}{OutputTail}");
            try
            {
                using var res = await probe.GetAsync("/health");
                if (res.StatusCode == HttpStatusCode.OK) return;
            }
            catch (HttpRequestException) { /* not listening yet */ }
            catch (TaskCanceledException) { /* probe timed out; try again */ }

            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"API instance {Name} was not healthy within {timeout.TotalSeconds:F0}s.{Environment.NewLine}{OutputTail}");
            await Task.Delay(200);
        }
    }

    /// <summary>The Api project's own build output, found from the test assembly's location (same configuration and target framework).</summary>
    private static string FindApiDll()
    {
        var baseDir = new DirectoryInfo(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        var targetFramework = baseDir.Name;
        var configuration = baseDir.Parent!.Name;

        for (var dir = baseDir; dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "src", "Api", "Api.csproj"))) continue;
            var dll = Path.Combine(dir.FullName, "src", "Api", "bin", configuration, targetFramework, "Api.dll");
            return File.Exists(dll)
                ? dll
                : throw new FileNotFoundException($"The Api project has not been built for {configuration}/{targetFramework}.", dll);
        }
        throw new DirectoryNotFoundException($"Could not find src/Api/Api.csproj above {AppContext.BaseDirectory}.");
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await _process.WaitForExitAsync(wait.Token);
            }
        }
        catch (InvalidOperationException) { /* never started, or already gone */ }
        catch (OperationCanceledException) { /* did not exit in time; nothing more to do */ }
        _process.Dispose();
    }
}

/// <summary>
/// A fresh Postgres (Testcontainers) and two API processes pointed at it, started one after the other: the first applies the
/// migrations at startup, the second finds them already applied, so two instances never race to migrate an empty database.
/// </summary>
public sealed class TwoApiInstancesFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:16").Build();
    private ApiProcess? _a, _b;

    public string ConnectionString => _pg.GetConnectionString();
    public ApiProcess A => _a ?? throw new InvalidOperationException("Instance A is not running.");
    public ApiProcess B => _b ?? throw new InvalidOperationException("Instance B is not running.");

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        try
        {
            _a = await ApiProcess.StartAsync("A", ConnectionString);
            _b = await ApiProcess.StartAsync("B", ConnectionString);
        }
        catch
        {
            await DisposeAsync(); // xunit does not dispose a fixture whose InitializeAsync threw
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (_b is not null) await _b.DisposeAsync();
        if (_a is not null) await _a.DisposeAsync();
        await _pg.DisposeAsync();
    }
}
