using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace CurlImpersonate.Http.Tests;

public sealed class ResponseStreamingTests
{
    [Fact]
    public async Task SendAsync_StreamResponseBodies_ReturnsAfterHeaders()
    {
        using var server = new StreamingServer();
        var bodyRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            FollowRedirects = false,
            StreamResponseBodies = true
        });
        using var client = new HttpClient(handler);

        var serverTask = server.AcceptAndStreamAsync("stream-body", bodyRelease.Task);
        using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);
        var responseTask = client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);

        await server.HeadersSent.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        using var response = await responseTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(11, response.Content.Headers.ContentLength);
        Assert.False(serverTask.IsCompleted);

        bodyRelease.SetResult();
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        await serverTask;

        Assert.Equal("stream-body", body);
        Assert.True(response.TryGetTransferMetrics(out var metrics));
        Assert.NotNull(metrics);
    }

    [Fact]
    public async Task SendAsync_StreamResponseBodies_EnforcesResponseLimitWhileReading()
    {
        using var server = new StreamingServer();
        var bodyRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            FollowRedirects = false,
            MaxResponseBodyBytes = 4,
            StreamResponseBodies = true
        });
        using var client = new HttpClient(handler);

        var serverTask = server.AcceptAndStreamAsync("too-large", bodyRelease.Task);
        using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);

        bodyRelease.SetResult();
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        await serverTask;

        // The body read surfaces the failure as HttpIOException; buffering via
        // ReadAsByteArrayAsync wraps that in HttpRequestException.
        var ioException = Assert.IsType<HttpIOException>(exception.InnerException);
        var curlException = Assert.IsType<CurlException>(ioException.InnerException);
        Assert.Contains("WriteError", curlException.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidOperationException>(curlException.InnerException);
    }

    [Fact]
    public async Task SendAsync_StreamResponseBodies_DisposeBeforeBody_CancelsTransfer()
    {
        using var server = new StreamingServer();
        var bodyRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            FollowRedirects = false,
            StreamResponseBodies = true
        });
        using var client = new HttpClient(handler);

        var serverTask = server.AcceptAndStreamAsync("ignored", bodyRelease.Task);
        using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);

        response.Dispose();
        bodyRelease.SetResult();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SendAsync_StreamResponseBodies_HandlerDisposeBeforeBody_CancelsTransfer()
    {
        using var server = new StreamingServer();
        var bodyRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            FollowRedirects = false,
            StreamResponseBodies = true
        });
        using var client = new HttpClient(handler);

        var serverTask = server.AcceptAndStreamAsync("ignored", bodyRelease.Task);
        using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);

        try
        {
            handler.Dispose();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }
        finally
        {
            bodyRelease.SetResult();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task SendAsync_StreamResponseBodies_SurvivesDisposeOfOtherHandler()
    {
        // Handler A: streaming, blocked mid-stream after headers.
        using var serverA = new StreamingServer();
        var releaseA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handlerA = new CurlHandler(new CurlHandlerOptions
        {
            FollowRedirects = false,
            StreamResponseBodies = true
        });
        using var clientA = new HttpClient(handlerA);

        var serverTaskA = serverA.AcceptAndStreamAsync("payload-A", releaseA.Task);
        using var requestA = new HttpRequestMessage(HttpMethod.Get, serverA.BaseUri);
        using var responseA = await clientA.SendAsync(
            requestA,
            HttpCompletionOption.ResponseHeadersRead,
            TestContext.Current.CancellationToken);

        await serverA.HeadersSent.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Handler B: default options (shared event loop), runs a complete request
        // then is disposed while A is still mid-stream.
        using (var serverB = new StreamingServer())
        {
            var releaseB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var serverTaskB = serverB.AcceptAndStreamAsync("payload-B", releaseB.Task);
            var handlerB = new CurlHandler(); // default => shared event loop
            var clientB = new HttpClient(handlerB);
            using var requestB = new HttpRequestMessage(HttpMethod.Get, serverB.BaseUri);
            var sendB = clientB.SendAsync(requestB, TestContext.Current.CancellationToken);
            releaseB.SetResult();
            using var responseB = await sendB.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal("payload-B", await responseB.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            await serverTaskB.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            clientB.Dispose();
            handlerB.Dispose(); // dispose the shared-loop handler while A is mid-stream
        }

        // Handler A's stream must still complete cleanly after B's disposal.
        releaseA.SetResult();
        var bodyA = await responseA.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        await serverTaskA.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal("payload-A", bodyA);
    }

    private sealed class StreamingServer : IDisposable
    {
        private readonly TcpListener _listener;

        public StreamingServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();

            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            BaseUri = new Uri($"http://127.0.0.1:{endpoint.Port}/");
        }

        public Uri BaseUri { get; }

        public TaskCompletionSource HeadersSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task AcceptAndStreamAsync(string body, Task bodyRelease)
        {
            using var client = await _listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            await using var stream = client.GetStream();
            await ReadRequestHeadersAsync(stream);

            var bodyBytes = Encoding.UTF8.GetBytes(body);
            var headers = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headers, TestContext.Current.CancellationToken);
            await stream.FlushAsync(TestContext.Current.CancellationToken);
            HeadersSent.SetResult();

            try
            {
                await bodyRelease.WaitAsync(TestContext.Current.CancellationToken);
                await stream.WriteAsync(bodyBytes, TestContext.Current.CancellationToken);
                await stream.FlushAsync(TestContext.Current.CancellationToken);
            }
            catch (IOException)
            {
                // The client may dispose the streaming response before the body
                // is written, which correctly closes the socket.
            }
        }

        private static async Task ReadRequestHeadersAsync(NetworkStream stream)
        {
            var buffer = new byte[4096];
            var received = new StringBuilder();

            while (true)
            {
                var read = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);
                if (read == 0)
                    break;

                received.Append(Encoding.ASCII.GetString(buffer, 0, read));
                if (received.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                    break;
            }
        }

        public void Dispose()
        {
            _listener.Stop();
        }
    }
}
