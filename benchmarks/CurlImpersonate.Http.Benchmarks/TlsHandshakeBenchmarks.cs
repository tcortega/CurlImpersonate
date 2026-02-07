using BenchmarkDotNet.Attributes;

namespace CurlImpersonate.Http.Benchmarks;

/// <summary>
/// Benchmarks measuring TLS handshake overhead (cold start).
/// Critical for curl-impersonate since TLS fingerprinting is its core feature.
/// Each invocation creates a new handler to measure fresh TLS negotiation.
/// </summary>
[MemoryDiagnoser]
[RankColumn]
[WarmupCount(BenchmarkConfig.WarmupCount)]
[IterationCount(BenchmarkConfig.IterationCount)]
[InvocationCount(BenchmarkConfig.InvocationCount)]
public class TlsHandshakeBenchmarks
{
    private static BenchmarkServerProcess _server = null!;
    private string _url = null!;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _server = new();
        await _server.StartAsync();
        _url = $"{BenchmarkServerProcess.BaseUrl}/get";
    }

    [GlobalCleanup]
    public void GlobalCleanup() => _server.Dispose();

    [Benchmark(Baseline = true)]
    public async Task<string> Native_ColdStart()
    {
        using var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
        using var client = new HttpClient(handler);
        return await client.GetStringAsync(_url);
    }

    [Benchmark]
    public async Task<string> Curl_ColdStart()
    {
        using var handler = new CurlHandler(new()
        {
            InsecureSkipVerify = true
        });
        using var client = new HttpClient(handler);
        return await client.GetStringAsync(_url);
    }
}
