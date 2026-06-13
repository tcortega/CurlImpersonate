using BenchmarkDotNet.Attributes;

namespace CurlImpersonate.Http.Benchmarks;

/// <summary>
/// Benchmarks comparing GET request performance between native HttpClient and CurlHandler.
/// </summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[RankColumn]
[WarmupCount(BenchmarkConfig.WarmupCount)]
[IterationCount(BenchmarkConfig.IterationCount)]
[InvocationCount(BenchmarkConfig.InvocationCount)]
public class GetRequestBenchmarks
{
    private static BenchmarkServerProcess _server = null!;
    private HttpClient _nativeClient = null!;
    private HttpClient _curlClient = null!;

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
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _nativeClient.Dispose();
        _curlClient.Dispose();
        _server.Dispose();
    }

    [Benchmark(Baseline = true)]
    public async Task<string> Native_SimpleGet()
        => await _nativeClient.GetStringAsync($"{BenchmarkServerProcess.BaseUrl}/get");

    [Benchmark]
    public async Task<string> Curl_SimpleGet()
        => await _curlClient.GetStringAsync($"{BenchmarkServerProcess.BaseUrl}/get");

    [Benchmark]
    public async Task<byte[]> Native_Get100Bytes()
        => await _nativeClient.GetByteArrayAsync($"{BenchmarkServerProcess.BaseUrl}/bytes/100");

    [Benchmark]
    public async Task<byte[]> Curl_Get100Bytes()
        => await _curlClient.GetByteArrayAsync($"{BenchmarkServerProcess.BaseUrl}/bytes/100");

    [Benchmark]
    public async Task<byte[]> Native_Get10KB()
        => await _nativeClient.GetByteArrayAsync($"{BenchmarkServerProcess.BaseUrl}/bytes/10240");

    [Benchmark]
    public async Task<byte[]> Curl_Get10KB()
        => await _curlClient.GetByteArrayAsync($"{BenchmarkServerProcess.BaseUrl}/bytes/10240");

    [Benchmark]
    public async Task<byte[]> Native_Get1MB()
        => await _nativeClient.GetByteArrayAsync($"{BenchmarkServerProcess.BaseUrl}/bytes/1048576");

    [Benchmark]
    public async Task<byte[]> Curl_Get1MB()
        => await _curlClient.GetByteArrayAsync($"{BenchmarkServerProcess.BaseUrl}/bytes/1048576");
}
