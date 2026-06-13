using System.Net;
using System.Net.Sockets;
using System.Text;
using CurlImpersonate.Enums;
using Xunit;

namespace CurlImpersonate.Http.Tests;

public sealed class RequestProfileOverrideTests
{
    [Fact]
    public async Task SendAsync_WithRequestProfileOption_OverridesHandlerProfile()
    {
        using var server = new LoopbackHttpServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            BrowserProfile = BrowserProfile.Chrome142
        });
        using var client = new HttpClient(handler);

        using var overridden = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);
        overridden.Options.Set(CurlRequestOptions.BrowserProfile, BrowserProfile.Firefox144);

        var requestTask = server.AcceptAndRespondAsync();
        using (await client.SendAsync(overridden, TestContext.Current.CancellationToken))
        {
        }

        var overriddenText = await requestTask;
        Assert.Contains("Firefox", overriddenText, StringComparison.Ordinal);
        Assert.DoesNotContain("Chrome", overriddenText, StringComparison.Ordinal);

        // The same pooled handler must fall back to its configured profile
        // when the next request carries no override.
        using var plain = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);
        requestTask = server.AcceptAndRespondAsync();
        using (await client.SendAsync(plain, TestContext.Current.CancellationToken))
        {
        }

        var plainText = await requestTask;
        Assert.Contains("Chrome", plainText, StringComparison.Ordinal);
        Assert.DoesNotContain("Firefox", plainText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_WithUndefinedRequestProfile_Throws()
    {
        using var server = new LoopbackHttpServer();
        using var handler = new CurlHandler();
        using var client = new HttpClient(handler);

        using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUri);
        request.Options.Set(CurlRequestOptions.BrowserProfile, (BrowserProfile)int.MaxValue);

        await Assert.ThrowsAsync<ArgumentException>(
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
            var buffer = new byte[8192];
            var received = new StringBuilder();

            while (true)
            {
                var read = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);
                if (read == 0)
                    return received.ToString();

                received.Append(Encoding.ASCII.GetString(buffer, 0, read));
                if (received.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                    return received.ToString();
            }
        }

        public void Dispose() => _listener.Stop();
    }
}
