namespace CurlImpersonate.Http.Benchmarks;

/// <summary>
/// Shared benchmark configuration constants.
/// These values prevent resource exhaustion while providing statistically meaningful results.
/// </summary>
public static class BenchmarkConfig
{
    /// <summary>
    /// Number of warmup iterations before measurement.
    /// </summary>
    public const int WarmupCount = 3;

    /// <summary>
    /// Number of actual measurement iterations.
    /// </summary>
    public const int IterationCount = 15;

    /// <summary>
    /// Number of invocations per iteration for simple benchmarks.
    /// Kept low to prevent TCP connection exhaustion (TIME_WAIT accumulation).
    /// </summary>
    public const int InvocationCount = 16;

    /// <summary>
    /// For benchmarks that internally do multiple requests (loops, parallel),
    /// use 1 invocation since the benchmark method itself handles multiplicity.
    /// </summary>
    public const int InvocationCountMultiRequest = 1;

    /// <summary>
    /// More iterations for multi-request benchmarks to compensate for InvocationCount=1.
    /// </summary>
    public const int IterationCountMultiRequest = 20;
}
