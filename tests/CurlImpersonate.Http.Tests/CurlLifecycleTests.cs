using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using CurlImpersonate.Enums;
using CurlImpersonate.Http.Internal;
using Xunit;

namespace CurlImpersonate.Http.Tests;

public sealed class CurlLifecycleTests
{
    [Fact]
    public void Options_DefaultToSharedEventLoop()
    {
        Assert.True(new CurlHandlerOptions().UseSharedEventLoop);
    }

    [Fact]
    public async Task TransferState_DisposeUnregistersCancellation()
    {
        using var loop = new CurlEventLoop();
        using var wrapper = new CurlEasyWrapper();
        using var cts = new CancellationTokenSource();

        var completion = new TaskCompletionSource<CurlResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transfer = new CurlTransferState(loop, wrapper, completion, cts.Token);

        wrapper.BeginTransfer(cts.Token);
        transfer.RegisterCancellation();
        transfer.Dispose();

        await cts.CancelAsync();

        Assert.False(wrapper.IsAborted);
    }

    [Fact]
    public void Pool_ReturnDiscardsCanceledWrapper()
    {
        using var pool = new CurlHandlePool(new CurlHandlerOptions { MaxPoolSize = 1 });
        var wrapper = pool.Rent();

        wrapper.MarkCancellationRequested();
        pool.Return(wrapper);

        Assert.Throws<ObjectDisposedException>(() => wrapper.Handle);
    }

    [Fact]
    public void Pool_DisposeDisposesIdleWrappers()
    {
        var pool = new CurlHandlePool(new CurlHandlerOptions { MaxPoolSize = 1 });
        var wrapper = pool.Rent();

        pool.Return(wrapper);
        pool.Dispose();

        Assert.Throws<ObjectDisposedException>(() => wrapper.Handle);
    }

    [Fact]
    public void Pool_DisposeDefersActiveWrapperDisposalUntilReturn()
    {
        var pool = new CurlHandlePool(new CurlHandlerOptions { MaxPoolSize = 1 });
        var wrapper = pool.Rent();

        pool.Dispose();

        Assert.NotEqual(0, wrapper.Handle);

        pool.Return(wrapper);

        Assert.Throws<ObjectDisposedException>(() => wrapper.Handle);
    }

    [Fact]
    public void SetStringOption_EmbeddedNul_ThrowsArgumentException()
    {
        using var wrapper = new CurlEasyWrapper();

        var exception = Assert.Throws<ArgumentException>(
            () => wrapper.SetStringOption(CurlOption.Url, "http://example.test/\0ignored"));

        Assert.Equal("value", exception.ParamName);
        Assert.Contains(nameof(CurlOption.Url), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handler_DisposeCancelsInFlightSharedLoopRequest()
    {
        using var server = new DelayedResponseServer();
        using var handler = new CurlHandler(new CurlHandlerOptions { UseSharedEventLoop = true });
        using var client = new HttpClient(handler, disposeHandler: false);

        var serverTask = server.AcceptAndWaitAsync();
        var requestTask = client.GetAsync(server.BaseUri, TestContext.Current.CancellationToken);

        await server.RequestReceived.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        try
        {
            handler.Dispose();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => requestTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        }
        finally
        {
            server.ReleaseResponse();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public void Handler_DisposeDisposesHandlerCancellationSource()
    {
        var handler = new CurlHandler();
        var cancellationSource = GetHandlerCancellationSource(handler);

        handler.Dispose();

        Assert.Throws<ObjectDisposedException>(() => cancellationSource.Token);
    }

    private static CancellationTokenSource GetHandlerCancellationSource(CurlHandler handler)
    {
        var field = typeof(CurlHandler).GetField(
            "_disposeCancellation",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        return Assert.IsType<CancellationTokenSource>(field.GetValue(handler));
    }

    private sealed class DelayedResponseServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly TaskCompletionSource _releaseResponse = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public DelayedResponseServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();

            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            BaseUri = new Uri($"http://127.0.0.1:{endpoint.Port}/");
        }

        public Uri BaseUri { get; }

        public TaskCompletionSource RequestReceived { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseResponse() => _releaseResponse.TrySetResult();

        public async Task AcceptAndWaitAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            await using var stream = client.GetStream();
            await ReadRequestHeadersAsync(stream);
            RequestReceived.TrySetResult();

            await _releaseResponse.Task.WaitAsync(TestContext.Current.CancellationToken);

            try
            {
                var responseBytes = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(responseBytes, TestContext.Current.CancellationToken);
            }
            catch (IOException)
            {
                // Disposing the handler should cancel the request and may close
                // the socket before the delayed server writes a response.
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
            _releaseResponse.TrySetResult();
            _listener.Stop();
        }
    }
}
