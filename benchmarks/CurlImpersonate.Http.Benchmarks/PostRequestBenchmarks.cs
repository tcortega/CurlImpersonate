using BenchmarkDotNet.Attributes;

namespace CurlImpersonate.Http.Benchmarks;

/// <summary>
/// Benchmarks comparing POST request performance with varying body sizes.
/// </summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[RankColumn]
[WarmupCount(BenchmarkConfig.WarmupCount)]
[IterationCount(BenchmarkConfig.IterationCount)]
[InvocationCount(BenchmarkConfig.InvocationCount)]
public class PostRequestBenchmarks
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
    public async Task<HttpResponseMessage> Native_PostSmall()
        => await _nativeClient.PostAsync($"{BenchmarkServerProcess.BaseUrl}/post",
            new ByteArrayContent(new byte[100]));

    [Benchmark]
    public async Task<HttpResponseMessage> Curl_PostSmall()
        => await _curlClient.PostAsync($"{BenchmarkServerProcess.BaseUrl}/post",
            new ByteArrayContent(new byte[100]));

    [Benchmark]
    public async Task<HttpResponseMessage> Native_Post10KB()
        => await _nativeClient.PostAsync($"{BenchmarkServerProcess.BaseUrl}/post",
            new ByteArrayContent(new byte[10240]));

    [Benchmark]
    public async Task<HttpResponseMessage> Curl_Post10KB()
        => await _curlClient.PostAsync($"{BenchmarkServerProcess.BaseUrl}/post",
            new ByteArrayContent(new byte[10240]));

    [Benchmark]
    public async Task<HttpResponseMessage> Native_Post1MB()
        => await _nativeClient.PostAsync($"{BenchmarkServerProcess.BaseUrl}/post",
            new ByteArrayContent(new byte[1048576]));

    [Benchmark]
    public async Task<HttpResponseMessage> Curl_Post1MB()
        => await _curlClient.PostAsync($"{BenchmarkServerProcess.BaseUrl}/post",
            new ByteArrayContent(new byte[1048576]));
}
