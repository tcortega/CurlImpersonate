using System.Net;
using System.Net.Sockets;
using System.Text;
using CurlImpersonate.Enums;
using CurlImpersonate.Http.Internal;
using Xunit;

namespace CurlImpersonate.Http.Tests;

/// <summary>
/// Regression tests that lock in fixes for previously broken behaviors. Each
/// test reproduces a specific scenario and asserts the corrected behavior so a
/// regression turns the test red again.
/// </summary>
public sealed class RegressionTests
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void PoolOverflow_DisposesNonRetainedWrapper()
    {
        using var pool = new CurlHandlePool(new CurlHandlerOptions { MaxPoolSize = 1 });
        var first = pool.Rent();
        var second = pool.Rent();

        pool.Return(first);
        pool.Return(second);

        // The pool retains at most one wrapper; the other return cannot be
        // kept and must be disposed instead of silently dropped.
        var stillReusable = (first.CanReuse ? 1 : 0) + (second.CanReuse ? 1 : 0);
        Assert.Equal(1, stillReusable);
    }

    [Fact]
    public async Task EventLoopDispose_DoesNotStrandQueuedTransfer()
    {
        using var server = new LoopbackServer();
        var loop = new CurlEventLoop();
        var wrapperA = new CurlEasyWrapper();
        var wrapperB = new CurlEasyWrapper();

        try
        {
            wrapperA.SetupCallbacks();
            wrapperA.SetStringOption(CurlOption.Url, server.BaseUri.AbsoluteUri);
            var taskA = loop.ExecuteAsync(wrapperA, CancellationToken.None);

            // The server accepts but never responds, so transfer A stays
            // pending and the loop thread sits in MultiPoll.
            using var stalledConnection = await server.AcceptAsync()
                .WaitAsync(StepTimeout, TestContext.Current.CancellationToken);

            wrapperB.SetupCallbacks();
            wrapperB.SetStringOption(CurlOption.Url, server.BaseUri.AbsoluteUri);
            var taskB = loop.ExecuteAsync(wrapperB, CancellationToken.None);
            loop.Dispose();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => taskB.WaitAsync(StepTimeout, TestContext.Current.CancellationToken));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => taskA.WaitAsync(StepTimeout, TestContext.Current.CancellationToken));
        }
        finally
        {
            loop.Dispose();
            wrapperA.Dispose();
            wrapperB.Dispose();
        }
    }

    [Fact]
    public async Task StreamingSlowConsumer_ReceivesFullBody()
    {
        const int BodyLength = 32 * 1024 * 1024;

        using var server = new LoopbackServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            FollowRedirects = false,
            StreamResponseBodies = true
        });
        using var client = new HttpClient(handler);

        var writeStalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = Task.Run(async () =>
        {
            using var connection = await server.AcceptAsync();
            await using var stream = connection.GetStream();
            await LoopbackServer.ReadRequestAsync(stream);

            var headerBytes = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Length: {BodyLength}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headerBytes, TestContext.Current.CancellationToken);

            var chunk = new byte[64 * 1024];
            Array.Fill(chunk, (byte)'a');
            var sent = 0;
            while (sent < BodyLength)
            {
                var count = Math.Min(chunk.Length, BodyLength - sent);
                var write = stream.WriteAsync(chunk.AsMemory(0, count), TestContext.Current.CancellationToken)
                    .AsTask();

                // A write that stays pending means transit buffers are full
                // behind the paused transfer: backpressure reached the server.
                if (!write.IsCompleted &&
                    await Task.WhenAny(write, Task.Delay(500, TestContext.Current.CancellationToken)) != write)
                {
                    writeStalled.TrySetResult();
                }

                await write;
                sent += count;
            }
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);
        using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                TestContext.Current.CancellationToken)
            .WaitAsync(StepTimeout, TestContext.Current.CancellationToken);

        // Read nothing until the server stalls, proving the consumer is slower
        // than the network, then drain concurrently with the remaining writes.
        // The body is far larger than transit buffers can absorb (pinned by
        // StreamingConsumerDeferringAllReads_StallsServerUntilDrained), so
        // the stall always happens.
        await writeStalled.Task.WaitAsync(StepTimeout, TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        await serverTask.WaitAsync(StepTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(BodyLength, body.Length);
    }

    [Fact]
    public async Task ConnectionRefused_ThrowsHttpRequestException()
    {
        int refusedPort;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        refusedPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        using var handler = new CurlHandler();
        using var client = new HttpClient(handler);

        await Assert.ThrowsAnyAsync<HttpRequestException>(
            () => client.GetAsync($"http://127.0.0.1:{refusedPort}/", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_DoesNotPostToCallerSynchronizationContext()
    {
        using var server = new LoopbackServer();
        using var handler = new CurlHandler();
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);

        var serverTask = server.AcceptAndRespondAsync(LoopbackServer.Response(200, "OK", "hello"));

        var context = new CountingSynchronizationContext();
        var original = SynchronizationContext.Current;
        Task<HttpResponseMessage> sendTask;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            sendTask = invoker.SendAsync(request, TestContext.Current.CancellationToken);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(original);
        }

        using var response = await sendTask.WaitAsync(StepTimeout, TestContext.Current.CancellationToken);
        await serverTask;

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(0, context.PostCount);
    }

    [Fact]
    public async Task Redirect_PreservesPerRequestProfileOverride()
    {
        using var server = new LoopbackServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            BrowserProfile = BrowserProfile.Chrome142,
            FollowRedirects = true
        });
        using var client = new HttpClient(handler);

        var serverTask = server.AcceptSequenceAsync(
            "HTTP/1.1 302 Found\r\nLocation: /final\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
            LoopbackServer.Response(200, "OK"));

        using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);
        request.Options.Set(CurlRequestOptions.BrowserProfile, BrowserProfile.Firefox144);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var requests = await serverTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Firefox", requests[1], StringComparison.Ordinal);
        Assert.DoesNotContain("Chrome", requests[1], StringComparison.Ordinal);
    }

    [Fact]
    public void StaleStreamingCancel_DoesNotAbortReusedWrapper()
    {
        using var loop = new CurlEventLoop();
        using var pool = new CurlHandlePool(new CurlHandlerOptions());
        var wrapper = pool.Rent();
        var state = new CurlStreamingResponseState(wrapper, pool, maxResponseBodyBytes: null);
        var transfer = new CurlTransferState(loop, wrapper, completion: null, CancellationToken.None, state);
        wrapper.BeginTransfer(CancellationToken.None);

        CurlEasyWrapper? reused = null;

        // CompleteBodyStream reads the completed flag, then invokes the
        // cancel callback. This callback reproduces the window in between:
        // the event loop completes the transfer, the wrapper is returned to
        // the pool and rented by an unrelated request, and only then does the
        // stale cancellation run.
        state.SetCancelTransfer(() =>
        {
            transfer.Dispose();
            state.CompleteSuccess(new CurlResponse(200, [], []));
            reused = pool.Rent();
            reused.BeginTransfer(CancellationToken.None);
            transfer.RequestCancellation();
        });

        state.CompleteBodyStream(cancelTransfer: true);

        Assert.NotNull(reused);
        var abortedAfterReuse = reused.IsAborted;
        pool.Discard(reused);

        Assert.Same(wrapper, reused);
        Assert.False(abortedAfterReuse);
    }

    [Fact]
    public async Task ExceedingMaxRedirects_ReturnsLastRedirectResponse()
    {
        using var server = new LoopbackServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            FollowRedirects = true,
            MaxRedirects = 1
        });
        using var client = new HttpClient(handler);

        var serverTask = server.AcceptSequenceAsync(
            "HTTP/1.1 302 Found\r\nLocation: /hop1\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
            "HTTP/1.1 302 Found\r\nLocation: /hop2\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

        using var response = await client.GetAsync(server.BaseUri, TestContext.Current.CancellationToken);
        await serverTask;

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }

    [Fact]
    public async Task AbandonedStreamingResponse_ReleasesTransferViaGarbageCollection()
    {
        using var server = new LoopbackServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            FollowRedirects = false,
            StreamResponseBodies = true
        });
        using var client = new HttpClient(handler);

        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Task.Run(async () =>
        {
            using var connection = await server.AcceptAsync();
            await using var stream = connection.GetStream();
            await LoopbackServer.ReadRequestAsync(stream);

            var partialResponse = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Length: 100\r\nConnection: close\r\n\r\n0123456789");
            await stream.WriteAsync(partialResponse, TestContext.Current.CancellationToken);

            try
            {
                var buffer = new byte[1];
                while (await stream.ReadAsync(buffer, TestContext.Current.CancellationToken) > 0)
                {
                }
            }
            catch (IOException)
            {
            }
            catch (OperationCanceledException)
            {
                return;
            }

            disconnected.TrySetResult();
        });

        await SendAndAbandonResponseAsync(client, server.BaseUri);

        var released = false;
        for (var attempt = 0; attempt < 20 && !released; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            released = await Task.WhenAny(
                disconnected.Task,
                Task.Delay(250, TestContext.Current.CancellationToken)) == disconnected.Task;
        }

        Assert.True(
            released,
            "Abandoning an undisposed streaming response must eventually cancel the transfer and release its handle.");
    }

    [Fact]
    public async Task UserTokenCancellationMidTransfer_ThrowsAndHandlerStaysUsable()
    {
        using var server = new LoopbackServer();
        using var handler = new CurlHandler();
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        using var cts = new CancellationTokenSource();

        var requestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stallTask = server.AcceptAndStallAsync(requestReceived, release.Task);

        using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);
        var sendTask = invoker.SendAsync(request, cts.Token);

        await requestReceived.Task.WaitAsync(StepTimeout, TestContext.Current.CancellationToken);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sendTask.WaitAsync(StepTimeout, TestContext.Current.CancellationToken));

        release.TrySetResult();
        await stallTask.WaitAsync(StepTimeout, TestContext.Current.CancellationToken);

        var serverTask = server.AcceptAndRespondAsync(LoopbackServer.Response(200, "OK", "after"));
        using var followUpRequest = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);
        using var response = await invoker.SendAsync(followUpRequest, TestContext.Current.CancellationToken)
            .WaitAsync(StepTimeout, TestContext.Current.CancellationToken);
        await serverTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UserTokenCancellationMidTransfer_BindsCallerTokenToException()
    {
        using var server = new LoopbackServer();
        using var handler = new CurlHandler();
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        using var cts = new CancellationTokenSource();

        var requestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stallTask = server.AcceptAndStallAsync(requestReceived, release.Task);

        using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);
        var sendTask = invoker.SendAsync(request, cts.Token);

        await requestReceived.Task.WaitAsync(StepTimeout, TestContext.Current.CancellationToken);
        cts.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sendTask.WaitAsync(StepTimeout, TestContext.Current.CancellationToken));
        Assert.Equal(cts.Token, exception.CancellationToken);

        release.TrySetResult();
        await stallTask.WaitAsync(StepTimeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ResponseTimeoutMidTransfer_SurfacesOperationTimedOut()
    {
        using var server = new LoopbackServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            Timeout = TimeSpan.FromMilliseconds(500)
        });
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);

        var requestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stallTask = server.AcceptAndStallAsync(requestReceived, release.Task);

        using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => invoker.SendAsync(request, TestContext.Current.CancellationToken)
                .WaitAsync(StepTimeout, TestContext.Current.CancellationToken));

        var curlException = Assert.IsType<CurlException>(exception.InnerException);
        Assert.Equal(CurlCode.OperationTimedOut, curlException.ErrorCode);

        release.TrySetResult();
        await stallTask.WaitAsync(StepTimeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TruncatedResponseBody_FailsTransferAndHandlerStaysUsable()
    {
        using var server = new LoopbackServer();
        using var handler = new CurlHandler();
        using var client = new HttpClient(handler);

        var serverTask = server.AcceptAndRespondAsync(
            "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 100\r\nConnection: close\r\n\r\n0123456789");

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync(server.BaseUri, TestContext.Current.CancellationToken));
        await serverTask;

        var curlException = Assert.IsType<CurlException>(exception.InnerException);
        Assert.Equal(CurlCode.PartialFile, curlException.ErrorCode);

        var followUpTask = server.AcceptAndRespondAsync(LoopbackServer.Response(200, "OK", "after"));
        using var response = await client.GetAsync(server.BaseUri, TestContext.Current.CancellationToken);
        await followUpTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Redirect307_PreservesMethodAndBody()
    {
        using var server = new LoopbackServer();
        using var handler = new CurlHandler(new CurlHandlerOptions { FollowRedirects = true });
        using var client = new HttpClient(handler);
        using var content = new StringContent("payload-307", Encoding.UTF8, "text/plain");

        var serverTask = server.AcceptSequenceAsync(
            "HTTP/1.1 307 Temporary Redirect\r\nLocation: /final\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
            LoopbackServer.Response(200, "OK"));

        using var response = await client.PostAsync(server.BaseUri, content, TestContext.Current.CancellationToken);
        var requests = await serverTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("POST /final HTTP/1.1", requests[1], StringComparison.Ordinal);
        Assert.Contains("payload-307", requests[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamingTruncatedBody_FailsBodyReadWithTransportException()
    {
        using var server = new LoopbackServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            FollowRedirects = false,
            StreamResponseBodies = true
        });
        using var client = new HttpClient(handler);

        var serverTask = Task.Run(async () =>
        {
            using var connection = await server.AcceptAsync();
            await using var stream = connection.GetStream();
            await LoopbackServer.ReadRequestAsync(stream);

            var partialResponse = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Length: 100\r\nConnection: close\r\n\r\n0123456789");
            await stream.WriteAsync(partialResponse, TestContext.Current.CancellationToken);
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);
        using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                TestContext.Current.CancellationToken)
            .WaitAsync(StepTimeout, TestContext.Current.CancellationToken);
        await serverTask.WaitAsync(StepTimeout, TestContext.Current.CancellationToken);

        var exception = await Record.ExceptionAsync(async () =>
        {
            await using var body = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
            var buffer = new byte[8192];
            while (await body.ReadAsync(buffer, TestContext.Current.CancellationToken) > 0)
            {
            }
        });

        Assert.NotNull(exception);
        Assert.True(
            exception is HttpRequestException or IOException,
            $"Mid-body transport failure surfaced {exception.GetType()} from the body read instead of HttpRequestException or IOException.");

        var followUpTask = server.AcceptAndRespondAsync(LoopbackServer.Response(200, "OK", "after"));
        using var followUp = await client.GetAsync(server.BaseUri, TestContext.Current.CancellationToken)
            .WaitAsync(StepTimeout, TestContext.Current.CancellationToken);
        await followUpTask;

        Assert.Equal(HttpStatusCode.OK, followUp.StatusCode);
    }

    [Fact]
    public async Task RedirectToUnsupportedScheme_ReturnsRedirectResponse()
    {
        using var server = new LoopbackServer();
        using var handler = new CurlHandler();
        using var client = new HttpClient(handler);

        var serverTask = server.AcceptAndRespondAsync(
            "HTTP/1.1 302 Found\r\nLocation: ftp://127.0.0.1/file\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

        using var response = await client.GetAsync(server.BaseUri, TestContext.Current.CancellationToken)
            .WaitAsync(StepTimeout, TestContext.Current.CancellationToken);
        await serverTask;

        // HttpClientHandler parity: a redirect to a non-http scheme is not
        // followed and the 3xx response is returned to the caller.
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
    }

    [Fact]
    public async Task StreamingConsumerDeferringAllReads_StallsServerUntilDrained()
    {
        const int BodyLength = 32 * 1024 * 1024;

        using var server = new LoopbackServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            FollowRedirects = false,
            StreamResponseBodies = true
        });
        using var client = new HttpClient(handler);

        var serverTask = Task.Run(async () =>
        {
            using var connection = await server.AcceptAsync();
            await using var stream = connection.GetStream();
            await LoopbackServer.ReadRequestAsync(stream);

            var headerBytes = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Length: {BodyLength}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headerBytes, TestContext.Current.CancellationToken);

            var chunk = new byte[64 * 1024];
            Array.Fill(chunk, (byte)'a');
            var sent = 0;
            while (sent < BodyLength)
            {
                var count = Math.Min(chunk.Length, BodyLength - sent);
                await stream.WriteAsync(chunk.AsMemory(0, count), TestContext.Current.CancellationToken);
                sent += count;
            }
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);
        using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                TestContext.Current.CancellationToken)
            .WaitAsync(StepTimeout, TestContext.Current.CancellationToken);

        // With no concurrent reader the paused transfer must stop the server
        // from finishing a body this far past the chunk queue capacity. The
        // StreamingSlowConsumer_ReceivesFullBody test awaits the server here
        // before reading anything, so it deadlocks whenever transit buffers
        // cannot absorb its body.
        var serverFinishedUnread = await Task.WhenAny(
            serverTask,
            Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken)) == serverTask;
        Assert.False(
            serverFinishedUnread,
            "Transit buffering absorbed the whole body with no reader, so backpressure never reached the server.");

        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        await serverTask.WaitAsync(StepTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(BodyLength, body.Length);
    }

    [Fact]
    public async Task DisposingPausedStreamingResponse_ClosesConnectionAndHandlerStaysUsable()
    {
        const long FloodLength = 256L * 1024 * 1024;

        using var server = new LoopbackServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            FollowRedirects = false,
            StreamResponseBodies = true
        });
        using var client = new HttpClient(handler);

        var writeStalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connectionClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = Task.Run(async () =>
        {
            using var connection = await server.AcceptAsync();
            await using var stream = connection.GetStream();
            await LoopbackServer.ReadRequestAsync(stream);

            var headerBytes = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Length: {FloodLength}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headerBytes, TestContext.Current.CancellationToken);

            var chunk = new byte[64 * 1024];
            Array.Fill(chunk, (byte)'b');
            var sent = 0L;
            try
            {
                while (sent < FloodLength)
                {
                    var write = stream.WriteAsync(chunk, TestContext.Current.CancellationToken).AsTask();

                    // A write that stays pending means every transit buffer is
                    // full behind the paused transfer's undrained chunk queue.
                    if (!write.IsCompleted &&
                        await Task.WhenAny(write, Task.Delay(500, TestContext.Current.CancellationToken)) != write)
                    {
                        writeStalled.TrySetResult();
                    }

                    await write;
                    sent += chunk.Length;
                }
            }
            catch (IOException)
            {
                connectionClosed.TrySetResult();
            }
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);
        var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                TestContext.Current.CancellationToken)
            .WaitAsync(StepTimeout, TestContext.Current.CancellationToken);

        await writeStalled.Task.WaitAsync(StepTimeout, TestContext.Current.CancellationToken);
        response.Dispose();

        await connectionClosed.Task.WaitAsync(StepTimeout, TestContext.Current.CancellationToken);

        var followUpTask = server.AcceptAndRespondAsync(LoopbackServer.Response(200, "OK", "after"));
        using var followUp = await client.GetAsync(server.BaseUri, TestContext.Current.CancellationToken)
            .WaitAsync(StepTimeout, TestContext.Current.CancellationToken);
        await followUpTask;

        Assert.Equal(HttpStatusCode.OK, followUp.StatusCode);
    }

    [Fact]
    public async Task PausedStreamingTransfer_OutlivesTimeoutWhileConsumerDrains()
    {
        const int BodyLength = 4 * 1024 * 1024;

        using var server = new LoopbackServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            FollowRedirects = false,
            StreamResponseBodies = true,
            Timeout = TimeSpan.FromSeconds(1)
        });
        using var client = new HttpClient(handler);

        var serverTask = Task.Run(async () =>
        {
            using var connection = await server.AcceptAsync();
            await using var stream = connection.GetStream();
            await LoopbackServer.ReadRequestAsync(stream);

            var headerBytes = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Length: {BodyLength}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headerBytes, TestContext.Current.CancellationToken);

            var chunk = new byte[64 * 1024];
            Array.Fill(chunk, (byte)'c');
            var sent = 0;
            try
            {
                while (sent < BodyLength)
                {
                    var count = Math.Min(chunk.Length, BodyLength - sent);
                    await stream.WriteAsync(chunk.AsMemory(0, count), TestContext.Current.CancellationToken);
                    sent += count;
                }
            }
            catch (IOException)
            {
                // The timeout abort under test resets the connection mid-flood.
            }
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);
        using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                TestContext.Current.CancellationToken)
            .WaitAsync(StepTimeout, TestContext.Current.CancellationToken);

        // The scenario under test: a healthy consumer that is merely slower
        // than Timeout, leaving the transfer paused past the 1 second mark.
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await serverTask.WaitAsync(StepTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(BodyLength, body.Length);
    }

    [Fact]
    public async Task StreamingHeadersWaitTimeout_ThrowsAndHandlerStaysUsable()
    {
        using var server = new LoopbackServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            FollowRedirects = false,
            StreamResponseBodies = true,
            Timeout = TimeSpan.FromSeconds(1)
        });
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);

        var requestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stallTask = server.AcceptAndStallAsync(requestReceived, release.Task);

        using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);
        var sendTask = invoker.SendAsync(request, TestContext.Current.CancellationToken);

        await requestReceived.Task.WaitAsync(StepTimeout, TestContext.Current.CancellationToken);

        // The server accepted but never sends the status line, so the streaming
        // headers wait must trip the configured Timeout and surface a transport
        // exception well before the test step timeout.
        await Assert.ThrowsAsync<HttpRequestException>(
            () => sendTask.WaitAsync(StepTimeout, TestContext.Current.CancellationToken));

        release.TrySetResult();
        await stallTask.WaitAsync(StepTimeout, TestContext.Current.CancellationToken);

        // The stalled transfer must have released its easy handle, so a
        // follow-up request on the same handler succeeds.
        var serverTask = server.AcceptAndRespondAsync(LoopbackServer.Response(200, "OK", "after"));
        using var followUpRequest = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);
        using var response = await invoker.SendAsync(followUpRequest, TestContext.Current.CancellationToken)
            .WaitAsync(StepTimeout, TestContext.Current.CancellationToken);
        await serverTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task StreamingHeadersWaitCallerCancel_ThrowsPromptlyWithCallerToken()
    {
        using var server = new LoopbackServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            FollowRedirects = false,
            StreamResponseBodies = true,
            Timeout = TimeSpan.FromSeconds(100)
        });
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        using var cts = new CancellationTokenSource();

        var requestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stallTask = server.AcceptAndStallAsync(requestReceived, release.Task);

        using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);
        var sendTask = invoker.SendAsync(request, cts.Token);

        await requestReceived.Task.WaitAsync(StepTimeout, TestContext.Current.CancellationToken);
        cts.Cancel();

        // Cancelling the caller token while the headers wait is still pending
        // must unblock it promptly, well under the 100 second Timeout, and
        // surface the caller's token rather than a header timeout.
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sendTask.WaitAsync(StepTimeout, TestContext.Current.CancellationToken));
        Assert.Equal(cts.Token, exception.CancellationToken);

        release.TrySetResult();
        await stallTask.WaitAsync(StepTimeout, TestContext.Current.CancellationToken);

        var serverTask = server.AcceptAndRespondAsync(LoopbackServer.Response(200, "OK", "after"));
        using var followUpRequest = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);
        using var response = await invoker.SendAsync(followUpRequest, TestContext.Current.CancellationToken)
            .WaitAsync(StepTimeout, TestContext.Current.CancellationToken);
        await serverTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task SendAndAbandonResponseAsync(HttpClient client, Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        _ = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);
    }

    private sealed class CountingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;

        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _postCount);
            ThreadPool.QueueUserWorkItem(static s =>
            {
                var (callback, callbackState) = ((SendOrPostCallback, object?))s!;
                callback(callbackState);
            }, (d, state));
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _postCount);
            d(state);
        }
    }

    private sealed class LoopbackServer : IDisposable
    {
        private readonly TcpListener _listener;

        public LoopbackServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();

            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            BaseUri = new Uri($"http://127.0.0.1:{endpoint.Port}/");
        }

        public Uri BaseUri { get; }

        public static string Response(int statusCode, string reasonPhrase, string body = "")
        {
            var contentLength = Encoding.UTF8.GetByteCount(body);
            return
                $"HTTP/1.1 {statusCode} {reasonPhrase}\r\n" +
                "Content-Type: text/plain\r\n" +
                $"Content-Length: {contentLength}\r\n" +
                "Connection: close\r\n" +
                "\r\n" +
                body;
        }

        public async Task<TcpClient> AcceptAsync()
        {
            return await _listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        }

        public async Task<string> AcceptAndRespondAsync(string response)
        {
            using var connection = await AcceptAsync();
            await using var stream = connection.GetStream();

            var request = await ReadRequestAsync(stream);
            var responseBytes = Encoding.UTF8.GetBytes(response);
            await stream.WriteAsync(responseBytes, TestContext.Current.CancellationToken);
            return request;
        }

        public async Task<IReadOnlyList<string>> AcceptSequenceAsync(params string[] responses)
        {
            var requests = new List<string>(responses.Length);
            foreach (var response in responses)
            {
                requests.Add(await AcceptAndRespondAsync(response));
            }

            return requests;
        }

        public async Task AcceptAndStallAsync(TaskCompletionSource requestReceived, Task release)
        {
            using var connection = await AcceptAsync();
            await using var stream = connection.GetStream();

            await ReadRequestAsync(stream);
            requestReceived.TrySetResult();
            await release.WaitAsync(TestContext.Current.CancellationToken);
        }

        public static async Task<string> ReadRequestAsync(NetworkStream stream)
        {
            var buffer = new byte[8192];
            var received = new List<byte>();

            while (true)
            {
                var read = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);
                if (read == 0)
                    break;

                received.AddRange(buffer.AsSpan(0, read).ToArray());
                var requestText = Encoding.UTF8.GetString(received.ToArray());
                var headerEnd = requestText.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (headerEnd < 0)
                    continue;

                var contentLength = GetContentLength(requestText[..headerEnd]);
                var bodyBytesRead = received.Count - headerEnd - 4;
                if (bodyBytesRead >= contentLength)
                    return requestText;
            }

            return Encoding.UTF8.GetString(received.ToArray());
        }

        private static int GetContentLength(string headers)
        {
            foreach (var line in headers.Split("\r\n"))
            {
                var colonIndex = line.IndexOf(':');
                if (colonIndex <= 0)
                    continue;

                if (!string.Equals(line[..colonIndex], "Content-Length", StringComparison.OrdinalIgnoreCase))
                    continue;

                return int.TryParse(line[(colonIndex + 1)..].Trim(), out var length) ? length : 0;
            }

            return 0;
        }

        public void Dispose()
        {
            _listener.Stop();
        }
    }
}
