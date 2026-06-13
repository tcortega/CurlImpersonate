using BenchmarkDotNet.Attributes;

namespace CurlImpersonate.Http.Benchmarks;

/// <summary>
/// Benchmarks measuring concurrent request handling performance.
/// </summary>
// ThreadingDiagnoser fails BenchmarkDotNet validation on this runtime and
// silently skips every benchmark in the class, so it stays off.
[MemoryDiagnoser]
[AllStatisticsColumn]
[RankColumn]
[WarmupCount(BenchmarkConfig.WarmupCount)]
[IterationCount(BenchmarkConfig.IterationCountMultiRequest)]
[InvocationCount(BenchmarkConfig.InvocationCountMultiRequest)]
public class ConcurrencyBenchmarks
{
    private static BenchmarkServerProcess _server = null!;
    private HttpClient _nativeClient = null!;
    private HttpClient _curlClient = null!;
    private HttpClient _curlOwnLoopClient = null!;

    [Params(10, 50, 100)]
    public int ConcurrentRequests { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _server = new();
        await _server.StartAsync();

        var nativeHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            MaxConnectionsPerServer = 200
        };
        _nativeClient = new(nativeHandler);
        
        _curlClient = new(new CurlHandler(new()
        {
            InsecureSkipVerify = true,
            MaxPoolSize = 200
        }));

        _curlOwnLoopClient = new(new CurlHandler(new()
        {
            InsecureSkipVerify = true,
            MaxPoolSize = 200,
            UseSharedEventLoop = false
        }));
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _nativeClient.Dispose();
        _curlClient.Dispose();
        _curlOwnLoopClient.Dispose();
        _server.Dispose();
    }

    [Benchmark(Baseline = true)]
    public async Task Native_ConcurrentGets()
    {
        var tasks = new Task<string>[ConcurrentRequests];
        for (var i = 0; i < ConcurrentRequests; i++)
        {
            tasks[i] = _nativeClient.GetStringAsync($"{BenchmarkServerProcess.BaseUrl}/get");
        }
        await Task.WhenAll(tasks);
    }

    [Benchmark]
    public async Task Curl_ConcurrentGets()
    {
        var tasks = new Task<string>[ConcurrentRequests];
        for (var i = 0; i < ConcurrentRequests; i++)
        {
            tasks[i] = _curlClient.GetStringAsync($"{BenchmarkServerProcess.BaseUrl}/get");
        }
        await Task.WhenAll(tasks);
    }

    [Benchmark]
    public async Task Curl_ConcurrentGets_OwnLoop()
    {
        var tasks = new Task<string>[ConcurrentRequests];
        for (var i = 0; i < ConcurrentRequests; i++)
        {
            tasks[i] = _curlOwnLoopClient.GetStringAsync($"{BenchmarkServerProcess.BaseUrl}/get");
        }
        await Task.WhenAll(tasks);
    }
}
