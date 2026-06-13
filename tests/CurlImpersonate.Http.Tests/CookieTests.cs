using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace CurlImpersonate.Http.Tests;

public sealed class CookieTests
{
    [Fact]
    public async Task SendAsync_StoresAndSendsCookieContainerCookies()
    {
        using var server = new LoopbackHttpServer();
        var options = new CurlHandlerOptions();
        using var handler = new CurlHandler(options);
        using var client = new HttpClient(handler);

        var firstRequest = server.AcceptAndRespondAsync(
            "HTTP/1.1 200 OK\r\nSet-Cookie: session=abc; Path=/\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

        using (await client.GetAsync(server.BaseUri, TestContext.Current.CancellationToken))
        {
        }

        await firstRequest;

        var secondRequest = server.AcceptAndRespondAsync(
            "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

        using (await client.GetAsync(server.BaseUri, TestContext.Current.CancellationToken))
        {
        }

        var requestText = await secondRequest;
        Assert.Contains("Cookie: session=abc", requestText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_ExplicitCookieHeaderOverridesContainerCookie()
    {
        using var server = new LoopbackHttpServer();
        var options = new CurlHandlerOptions();
        options.CookieContainer.Add(server.BaseUri, new Cookie("session", "container"));

        using var handler = new CurlHandler(options);
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);
        request.Headers.Add("Cookie", "session=explicit");

        var requestTask = server.AcceptAndRespondAsync(
            "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

        using (await client.SendAsync(request, TestContext.Current.CancellationToken))
        {
        }

        var requestText = await requestTask;
        Assert.Contains("Cookie: session=explicit", requestText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("session=container", requestText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_UseCookiesFalse_DoesNotSendOrStoreContainerCookies()
    {
        using var server = new LoopbackHttpServer();
        var options = new CurlHandlerOptions { UseCookies = false };
        options.CookieContainer.Add(server.BaseUri, new Cookie("session", "container"));

        using var handler = new CurlHandler(options);
        using var client = new HttpClient(handler);

        var firstRequest = server.AcceptAndRespondAsync(
            "HTTP/1.1 200 OK\r\nSet-Cookie: session=response; Path=/\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

        using (await client.GetAsync(server.BaseUri, TestContext.Current.CancellationToken))
        {
        }

        var firstRequestText = await firstRequest;

        var secondRequest = server.AcceptAndRespondAsync(
            "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

        using (await client.GetAsync(server.BaseUri, TestContext.Current.CancellationToken))
        {
        }

        var secondRequestText = await secondRequest;

        Assert.DoesNotContain("Cookie:", firstRequestText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cookie:", secondRequestText, StringComparison.OrdinalIgnoreCase);
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
