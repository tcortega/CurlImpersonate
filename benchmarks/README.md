# CurlImpersonate Benchmarks

Run benchmarks from the repository root:

```bash
dotnet run -c Release --project benchmarks/CurlImpersonate.Http.Benchmarks -- \
  --filter "*" \
  --artifacts artifacts/benchmarks
python3 tools/validate_benchmark_results.py --results-dir artifacts/benchmarks/results
```

Useful focused runs:

```bash
dotnet run -c Release --project benchmarks/CurlImpersonate.Http.Benchmarks -- --filter "*GetRequest*"
dotnet run -c Release --project benchmarks/CurlImpersonate.Http.Benchmarks -- --filter "*Concurrency*"
dotnet run -c Release --project benchmarks/CurlImpersonate.Http.Benchmarks -- --filter "*HandlerLifecycle*"
```

After a focused or full benchmark run, validate the generated CSV output:

```bash
python3 tools/validate_benchmark_results.py
```

CI runs the focused `Curl_Get100Bytes` gate on scheduled and manual workflow
runs, then uploads the full BenchmarkDotNet artifact directory.

To pin a release baseline for future regression checks:

```bash
python3 tools/validate_benchmark_results.py \
  --write-baseline benchmarks/baselines/<version>.json
```

Future runs can compare against that baseline:

```bash
python3 tools/validate_benchmark_results.py \
  --baseline benchmarks/baselines/<version>.json
```

Release acceptance gates:

- Small GET allocation budget: `GetRequestBenchmarks.Curl_Get100Bytes` must not exceed 256 KiB allocated/op or regress more than 15% from the last released baseline, whichever is lower.
- Large response behavior: `GetRequestBenchmarks.Curl_Get1MB` must not cause retained pooled response buffers above `ResponseBuffer.DefaultMaxRetainedCapacity`; this is also covered by `ResponseBufferTests`.
- Concurrent throughput: `ConcurrencyBenchmarks.Curl_ConcurrentGets` should scale through 100 concurrent requests without unbounded thread growth or handle creation.
- Handler lifecycle: default handlers use the shared event loop; `HandlerLifecycleBenchmarks.SharedEventLoop_CreateDisposeHandlers` must show materially lower thread overhead than owned event loops for 10+ handlers.
- Cancellation and cleanup: run the lifecycle/cancellation unit tests with the benchmark run and treat handle leaks or pending transfers as release blockers.

BenchmarkDotNet emits memory, threading, ranking, and full statistics columns so release notes can record p50/p95-style latency distribution, allocated bytes/op, and thread contention evidence.
