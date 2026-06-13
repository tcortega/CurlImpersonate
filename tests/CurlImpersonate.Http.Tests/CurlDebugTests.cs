using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace CurlImpersonate.Http.Tests;

public sealed class CurlDebugTests
{
    [Fact]
    public async Task SendAsync_WithCurlDebug_EmitsHeaderEvents()
    {
        var events = new ConcurrentQueue<CurlDebugEvent>();
        using var server = new LoopbackHttpServer();
        using var handler = new CurlHandler(new()
        {
            EnableCurlDebug = true,
            DebugCallback = events.Enqueue
        });
        using var client = new HttpClient(handler);

        var serverTask = server.AcceptAndRespondAsync(
            "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var response = await client.GetAsync(server.BaseUri, TestContext.Current.CancellationToken);
        await serverTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(events, e => e.Type == CurlDebugInfoType.HeaderOut &&
                                    e.GetText().Contains("GET / HTTP/1.1", StringComparison.Ordinal));
        Assert.Contains(events, e => e.Type == CurlDebugInfoType.HeaderIn &&
                                    e.GetText().Contains("HTTP/1.1 200 OK", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendAsync_WithThrowingCurlDebugCallback_DoesNotFailTransfer()
    {
        using var server = new LoopbackHttpServer();
        using var handler = new CurlHandler(new()
        {
            EnableCurlDebug = true,
            DebugCallback = _ => throw new InvalidOperationException("observer failed")
        });
        using var client = new HttpClient(handler);

        var serverTask = server.AcceptAndRespondAsync(
            "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var response = await client.GetAsync(server.BaseUri, TestContext.Current.CancellationToken);
        await serverTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class LoopbackHttpServer : IDisposable
    {
        private readonly TcpListener _listener;

        public LoopbackHttpServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();

            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            BaseUri = new Uri($"http://127.0.0.1:{endpoint.Port}/");
        }

        public Uri BaseUri { get; }

        public async Task<string> AcceptAndRespondAsync(string response)
        {
            using var client = await _listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            await using var stream = client.GetStream();

            var request = await ReadRequestHeadersAsync(stream);
            var responseBytes = Encoding.ASCII.GetBytes(response);
            await stream.WriteAsync(responseBytes, TestContext.Current.CancellationToken);
            return request;
        }

        private static async Task<string> ReadRequestHeadersAsync(NetworkStream stream)
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

            return received.ToString();
        }

        public void Dispose()
        {
            _listener.Stop();
        }
    }
}
