using BenchmarkDotNet.Attributes;

namespace CurlImpersonate.Http.Benchmarks;

/// <summary>
/// Benchmarks measuring warm connection performance (connection reuse / keep-alive).
/// Tests sequential requests to the same endpoint after initial connection is established.
/// </summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[RankColumn]
[WarmupCount(BenchmarkConfig.WarmupCount)]
[IterationCount(BenchmarkConfig.IterationCountMultiRequest)]
[InvocationCount(BenchmarkConfig.InvocationCountMultiRequest)]
public class ConnectionReuseBenchmarks
{
    private static BenchmarkServerProcess _server = null!;
    private HttpClient _nativeClient = null!;
    private HttpClient _curlClient = null!;

    [Params(1, 5, 10)]
    public int RequestCount { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _server = new();
        await _server.StartAsync();

        var nativeHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _nativeClient = new(nativeHandler);
        _curlClient = new(new CurlHandler(new()
        {
            InsecureSkipVerify = true
        }));

        // Warm up connections before benchmark
        await _nativeClient.GetStringAsync($"{BenchmarkServerProcess.BaseUrl}/get");
        await _curlClient.GetStringAsync($"{BenchmarkServerProcess.BaseUrl}/get");
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _nativeClient.Dispose();
        _curlClient.Dispose();
        _server.Dispose();
    }

    [Benchmark(Baseline = true)]
    public async Task Native_SequentialRequests()
    {
        for (var i = 0; i < RequestCount; i++)
        {
            await _nativeClient.GetStringAsync($"{BenchmarkServerProcess.BaseUrl}/get");
        }
    }

    [Benchmark]
    public async Task Curl_SequentialRequests()
    {
        for (var i = 0; i < RequestCount; i++)
        {
            await _curlClient.GetStringAsync($"{BenchmarkServerProcess.BaseUrl}/get");
        }
    }
}
