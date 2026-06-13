using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace CurlImpersonate.Http.Tests;

public sealed class ConnectionLimitTests
{
    [Fact]
    public void Constructor_InvalidOptions_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CurlHandler(new CurlHandlerOptions { MaxPoolSize = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CurlHandler(new CurlHandlerOptions { MaxTotalConnections = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CurlHandler(new CurlHandlerOptions { MaxConnectionsPerHost = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CurlHandler(new CurlHandlerOptions { MaxConnects = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CurlHandler(new CurlHandlerOptions { MaxRequestBodyBytes = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CurlHandler(new CurlHandlerOptions { MaxResponseBodyBytes = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CurlHandler(new CurlHandlerOptions { Timeout = TimeSpan.Zero }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CurlHandler(new CurlHandlerOptions { ConnectTimeout = TimeSpan.FromMilliseconds(-2) }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CurlHandler(new CurlHandlerOptions { PooledConnectionLifetime = TimeSpan.Zero }));
        Assert.Throws<ArgumentException>(
            () => new CurlHandler(new CurlHandlerOptions { StreamResponseBodies = true }));
        Assert.Throws<ArgumentNullException>(
            () => new CurlHandler(new CurlHandlerOptions { CookieContainer = null! }));
        Assert.Throws<ArgumentException>(
            () => new CurlHandler(new CurlHandlerOptions { ProxyUri = new Uri("/proxy", UriKind.Relative) }));
    }

    [Fact]
    public void Constructor_InfiniteTimeouts_DoesNotThrow()
    {
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            Timeout = Timeout.InfiniteTimeSpan,
            ConnectTimeout = Timeout.InfiniteTimeSpan
        });
    }

    [Fact]
    public async Task SendAsync_WithConnectionLimits_CompletesRequest()
    {
        using var server = new LoopbackHttpServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            MaxTotalConnections = 4,
            MaxConnectionsPerHost = 2,
            MaxConnects = 4,
            PooledConnectionLifetime = TimeSpan.FromSeconds(1)
        });
        using var client = new HttpClient(handler);

        var serverTask = server.AcceptAndRespondAsync();
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

        public async Task AcceptAndRespondAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            await using var stream = client.GetStream();
            await ReadRequestHeadersAsync(stream);

            var responseBytes = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(responseBytes, TestContext.Current.CancellationToken);
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
