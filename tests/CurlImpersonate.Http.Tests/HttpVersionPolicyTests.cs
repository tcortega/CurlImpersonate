using System.Net;
using System.Net.Sockets;
using System.Text;
using CurlImpersonate.Http.Internal;
using Xunit;

namespace CurlImpersonate.Http.Tests;

public sealed class HttpVersionPolicyTests
{
    [Fact]
    public void MapRequestVersion_DefaultRequest_UsesHandlerVersionPolicy()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        var options = new CurlHandlerOptions();

        var httpVersion = RequestMapper.MapRequestVersion(request, options);

        Assert.Equal(4, httpVersion);
    }

    [Fact]
    public void MapRequestVersion_RequestPolicyOverridesHandlerVersionPolicy()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        var options = new CurlHandlerOptions
        {
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };

        var httpVersion = RequestMapper.MapRequestVersion(request, options);

        Assert.Equal(5, httpVersion);
    }

    [Theory]
    [InlineData(HttpVersionPolicy.RequestVersionOrHigher, 30)]
    [InlineData(HttpVersionPolicy.RequestVersionExact, 31)]
    public void MapRequestVersion_Http3WithOptIn_MapsPolicy(
        HttpVersionPolicy policy,
        long expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Version = HttpVersion.Version30,
            VersionPolicy = policy
        };
        var options = new CurlHandlerOptions
        {
            EnableHttp3 = true
        };

        var httpVersion = RequestMapper.MapRequestVersion(request, options);

        Assert.Equal(expected, httpVersion);
    }

    [Fact]
    public void MapRequestVersion_Http3WithoutOptIn_Throws()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Version = HttpVersion.Version30,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        var options = new CurlHandlerOptions();

        Assert.Throws<NotSupportedException>(() => RequestMapper.MapRequestVersion(request, options));
    }

    [Fact]
    public async Task SendAsync_RequestVersion10Exact_SendsHttp10Request()
    {
        using var server = new LoopbackHttpServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            HeaderPolicy = BrowserHeaderPolicy.DisableBrowserHeaders
        });
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, server.BaseUri)
        {
            Version = HttpVersion.Version10,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

        var requestTask = server.AcceptAndRespondAsync();
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var requestText = await requestTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("GET / HTTP/1.0", requestText, StringComparison.Ordinal);
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
                "HTTP/1.0 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
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
