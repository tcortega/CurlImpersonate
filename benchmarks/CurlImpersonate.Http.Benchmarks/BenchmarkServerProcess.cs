using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CurlImpersonate.Http.Benchmarks;

/// <summary>
/// Manages the lifecycle of the benchmark server process.
/// The server runs in a separate process for crash isolation and accurate memory measurement.
/// </summary>
public sealed class BenchmarkServerProcess : IDisposable
{
    private Process? _serverProcess;
    private const int Port = 5199;

    public static string BaseUrl => $"https://localhost:{Port}";

    public async Task StartAsync()
    {
        var serverProject = FindServerProject();

        _serverProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{serverProject}\" -c Release",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        });

        if (_serverProcess == null)
            throw new InvalidOperationException("Failed to start server process");

        // Poll /health until ready (skip cert validation for dev cert)
        using var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        using var client = new HttpClient(handler);

        for (var i = 0; i < 50; i++)
        {
            try
            {
                var response = await client.GetAsync($"{BaseUrl}/health");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch
            {
                // Server not ready yet
            }
            await Task.Delay(100);
        }

        throw new InvalidOperationException("Server failed to start within timeout");
    }

    private static string FindServerProject([CallerFilePath] string? callerPath = null)
    {
        if (callerPath != null)
        {
            var benchmarksDir = Path.GetDirectoryName(Path.GetDirectoryName(callerPath));
            if (benchmarksDir != null)
            {
                var serverProject = Path.Combine(benchmarksDir,
                    "CurlImpersonate.Http.Benchmarks.Server",
                    "CurlImpersonate.Http.Benchmarks.Server.csproj");
                if (File.Exists(serverProject))
                    return serverProject;
            }
        }

        // Fallback: try relative to current directory
        var candidates = new[]
        {
            "benchmarks/CurlImpersonate.Http.Benchmarks.Server/CurlImpersonate.Http.Benchmarks.Server.csproj",
            "../CurlImpersonate.Http.Benchmarks.Server/CurlImpersonate.Http.Benchmarks.Server.csproj",
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        // Walk up from the runtime directory: BenchmarkDotNet runs generated
        // projects nested under the original bin, and deterministic CI builds
        // rewrite CallerFilePath to the unmapped /_/ source root.
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName,
                "benchmarks",
                "CurlImpersonate.Http.Benchmarks.Server",
                "CurlImpersonate.Http.Benchmarks.Server.csproj");
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            $"Could not find benchmark server project. CallerPath={callerPath}, CurrentDir={Directory.GetCurrentDirectory()}");
    }

    public void Dispose()
    {
        if (_serverProcess is { HasExited: false })
        {
            try
            {
                _serverProcess.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process may have already exited
            }
        }
        _serverProcess?.Dispose();
    }
}
