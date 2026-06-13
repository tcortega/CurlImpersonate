using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace CurlImpersonate.Http.Tests;

public sealed class HeaderPolicyTests
{
    [Fact]
    public async Task SendAsync_DefaultPolicy_PreservesBrowserManagedHeader()
    {
        using var server = new LoopbackHttpServer();
        using var handler = new CurlHandler();
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);
        request.Headers.TryAddWithoutValidation("User-Agent", "test-agent");

        var requestTask = server.AcceptAndRespondAsync();
        using (await client.SendAsync(request, TestContext.Current.CancellationToken))
        {
        }

        var requestText = await requestTask;
        Assert.DoesNotContain("test-agent", requestText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_AllowUserOverride_SendsBrowserManagedHeader()
    {
        using var server = new LoopbackHttpServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            HeaderPolicy = BrowserHeaderPolicy.AllowUserOverride
        });
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);
        request.Headers.TryAddWithoutValidation("User-Agent", "test-agent");

        var requestTask = server.AcceptAndRespondAsync();
        using (await client.SendAsync(request, TestContext.Current.CancellationToken))
        {
        }

        var requestText = await requestTask;
        Assert.Contains("User-Agent: test-agent", requestText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_HeaderOrder_OrdersCustomHeaders()
    {
        using var server = new LoopbackHttpServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            HeaderPolicy = BrowserHeaderPolicy.DisableBrowserHeaders,
            HeaderOrder = ["X-B", "X-A"]
        });
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);
        request.Headers.TryAddWithoutValidation("X-A", "a");
        request.Headers.TryAddWithoutValidation("X-B", "b");

        var requestTask = server.AcceptAndRespondAsync();
        using (await client.SendAsync(request, TestContext.Current.CancellationToken))
        {
        }

        var requestText = await requestTask;
        var xAIndex = requestText.IndexOf("X-A: a", StringComparison.OrdinalIgnoreCase);
        var xBIndex = requestText.IndexOf("X-B: b", StringComparison.OrdinalIgnoreCase);

        Assert.True(xAIndex >= 0);
        Assert.True(xBIndex >= 0);
        Assert.True(xBIndex < xAIndex);
    }

    [Fact]
    public async Task SendAsync_HeaderValueWithLineBreak_ThrowsFormatException()
    {
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            HeaderPolicy = BrowserHeaderPolicy.AllowUserOverride
        });
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1/");

        Assert.True(request.Headers.TryAddWithoutValidation("X-Test", "ok\r\nInjected: yes"));

        await Assert.ThrowsAsync<FormatException>(
            () => client.SendAsync(request, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_HeaderValueWithEmbeddedNul_ThrowsFormatException()
    {
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            HeaderPolicy = BrowserHeaderPolicy.AllowUserOverride
        });
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1/");

        Assert.True(request.Headers.TryAddWithoutValidation("X-Test", "visible\0hidden"));

        await Assert.ThrowsAsync<FormatException>(
            () => client.SendAsync(request, TestContext.Current.CancellationToken));
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

        public async Task<string> AcceptAndRespondAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            await using var stream = client.GetStream();

            var request = await ReadRequestHeadersAsync(stream);
            var responseBytes = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
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
