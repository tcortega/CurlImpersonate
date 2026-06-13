using BenchmarkDotNet.Attributes;

namespace CurlImpersonate.Http.Benchmarks;

/// <summary>
/// Benchmarks handler creation and event-loop ownership overhead.
/// </summary>
[MemoryDiagnoser]
[ThreadingDiagnoser]
[AllStatisticsColumn]
[RankColumn]
[WarmupCount(BenchmarkConfig.WarmupCount)]
[IterationCount(BenchmarkConfig.IterationCount)]
[InvocationCount(BenchmarkConfig.InvocationCountMultiRequest)]
public class HandlerLifecycleBenchmarks
{
    [Params(1, 10, 50)]
    public int HandlerCount { get; set; }

    [Benchmark(Baseline = true)]
    public void OwnedEventLoop_CreateDisposeHandlers()
    {
        for (var i = 0; i < HandlerCount; i++)
        {
            using var handler = new CurlHandler(new()
            {
                UseSharedEventLoop = false
            });
        }
    }

    [Benchmark]
    public void SharedEventLoop_CreateDisposeHandlers()
    {
        for (var i = 0; i < HandlerCount; i++)
        {
            using var handler = new CurlHandler(new()
            {
                UseSharedEventLoop = true
            });
        }
    }
}
