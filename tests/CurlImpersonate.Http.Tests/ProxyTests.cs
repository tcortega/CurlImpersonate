using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace CurlImpersonate.Http.Tests;

public sealed class ProxyTests
{
    [Fact]
    public async Task SendAsync_WithProxyUri_UsesProxy()
    {
        using var proxy = new LoopbackProxyServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            ProxyUri = proxy.ProxyUri
        });
        using var client = new HttpClient(handler);

        var proxyTask = proxy.AcceptAndRespondAsync(
            "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var response = await client.GetAsync("http://example.test/resource", TestContext.Current.CancellationToken);
        var requestText = await proxyTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("GET http://example.test/resource HTTP/1.1", requestText, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_WithProxyAndProxyUri_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new CurlHandler(new CurlHandlerOptions
            {
                Proxy = new WebProxy("http://proxy.test:8080"),
                ProxyUri = new Uri("http://proxy.test:8080")
            }));
    }

    [Fact]
    public async Task SendAsync_WithNoProxy_BypassesProxy()
    {
        using var origin = new LoopbackOriginServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            ProxyUri = GetUnusedLoopbackProxyUri(),
            NoProxy = "127.0.0.1"
        });
        using var client = new HttpClient(handler);

        var originTask = origin.AcceptAndRespondAsync(
            "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var response = await client.GetAsync(origin.BaseUri, TestContext.Current.CancellationToken);
        var requestText = await originTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("GET / HTTP/1.1", requestText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_WithProxyCredentials_AuthenticatesAfterChallenge()
    {
        using var proxy = new LoopbackProxyServer();
        var options = new CurlHandlerOptions
        {
            Proxy = new WebProxy(proxy.ProxyUri),
            ProxyCredentials = new NetworkCredential("user", "pass")
        };

        using var handler = new CurlHandler(options);
        using var client = new HttpClient(handler);

        var proxyTask = proxy.AcceptChallengeThenOkAsync();
        using var response = await client.GetAsync("http://example.test/resource", TestContext.Current.CancellationToken);
        var (_, authenticatedRequest) = await proxyTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Proxy-Authorization: Basic dXNlcjpwYXNz", authenticatedRequest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_WithBasicProxyAuth_SendsPreemptiveCredentials()
    {
        using var proxy = new LoopbackProxyServer();
        var options = new CurlHandlerOptions
        {
            Proxy = new WebProxy(proxy.ProxyUri),
            ProxyCredentials = new NetworkCredential("user", "pass"),
            ProxyAuth = CurlProxyAuth.Basic
        };

        using var handler = new CurlHandler(options);
        using var client = new HttpClient(handler);

        var proxyTask = proxy.AcceptAndRespondAsync(
            "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var response = await client.GetAsync("http://example.test/resource", TestContext.Current.CancellationToken);
        var requestText = await proxyTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Proxy-Authorization: Basic dXNlcjpwYXNz", requestText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_WithAuthenticatedSocks5Proxy_UsesProxyCredentials()
    {
        using var proxy = new LoopbackSocks5ProxyServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            ProxyUri = proxy.ProxyUri,
            ProxyCredentials = new NetworkCredential("user", "pass")
        });
        using var client = new HttpClient(handler);

        var proxyTask = proxy.AcceptAndRespondAsync(
            "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var response = await client.GetAsync("http://example.test/resource", TestContext.Current.CancellationToken);
        var result = await proxyTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("user", result.UserName);
        Assert.Equal("pass", result.Password);
        Assert.Equal("example.test", result.RequestedHost);
        Assert.StartsWith("GET /resource HTTP/1.1", result.HttpRequest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_WithSocks5ProxyCredentials_AllowsSeparatorCharacters()
    {
        using var proxy = new LoopbackSocks5ProxyServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            ProxyUri = proxy.ProxyUri,
            ProxyCredentials = new NetworkCredential("user:name", "pa:ss")
        });
        using var client = new HttpClient(handler);

        var proxyTask = proxy.AcceptAndRespondAsync(
            "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var response = await client.GetAsync("http://example.test/resource", TestContext.Current.CancellationToken);
        var result = await proxyTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("user:name", result.UserName);
        Assert.Equal("pa:ss", result.Password);
    }

    [Fact]
    public async Task SendAsync_WithWebProxy_UsesProxy()
    {
        using var proxy = new LoopbackProxyServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            Proxy = new WebProxy(proxy.ProxyUri)
        });
        using var client = new HttpClient(handler);

        var proxyTask = proxy.AcceptAndRespondAsync(
            "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var response = await client.GetAsync("http://example.test/resource", TestContext.Current.CancellationToken);
        var requestText = await proxyTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("GET http://example.test/resource HTTP/1.1", requestText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_WithWebProxyBypassList_GoesDirect()
    {
        using var origin = new LoopbackOriginServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            Proxy = new WebProxy(GetUnusedLoopbackProxyUri())
            {
                BypassList = [@"127\.0\.0\.1"]
            }
        });
        using var client = new HttpClient(handler);

        var originTask = origin.AcceptAndRespondAsync(
            "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var response = await client.GetAsync(origin.BaseUri, TestContext.Current.CancellationToken);
        var requestText = await originTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("GET / HTTP/1.1", requestText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_WithWebProxyCredentials_AuthenticatesAfterChallenge()
    {
        using var proxy = new LoopbackProxyServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            Proxy = new WebProxy(proxy.ProxyUri)
            {
                Credentials = new NetworkCredential("user", "pass")
            }
        });
        using var client = new HttpClient(handler);

        var proxyTask = proxy.AcceptChallengeThenOkAsync();
        using var response = await client.GetAsync("http://example.test/resource", TestContext.Current.CancellationToken);
        var (_, authenticatedRequest) = await proxyTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Proxy-Authorization: Basic dXNlcjpwYXNz", authenticatedRequest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_WithUseProxyFalse_IgnoresConfiguredProxy()
    {
        using var origin = new LoopbackOriginServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            ProxyUri = GetUnusedLoopbackProxyUri(),
            UseProxy = false
        });
        using var client = new HttpClient(handler);

        var originTask = origin.AcceptAndRespondAsync(
            "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var response = await client.GetAsync(origin.BaseUri, TestContext.Current.CancellationToken);
        var requestText = await originTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("GET / HTTP/1.1", requestText, StringComparison.Ordinal);
    }

    private sealed class LoopbackProxyServer : IDisposable
    {
        private readonly TcpListener _listener;

        public LoopbackProxyServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();

            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            ProxyUri = new Uri($"http://127.0.0.1:{endpoint.Port}/");
        }

        public Uri ProxyUri { get; }

        public async Task<string> AcceptAndRespondAsync(string response)
        {
            using var client = await _listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            await using var stream = client.GetStream();

            var request = await ReadRequestHeadersAsync(stream);
            var responseBytes = Encoding.ASCII.GetBytes(response);
            await stream.WriteAsync(responseBytes, TestContext.Current.CancellationToken);
            return request;
        }

        public async Task<(string ChallengeRequest, string AuthenticatedRequest)> AcceptChallengeThenOkAsync()
        {
            var challengeRequest = await AcceptAndRespondAsync(
                "HTTP/1.1 407 Proxy Authentication Required\r\n" +
                "Proxy-Authenticate: Basic realm=\"proxy\"\r\n" +
                "Content-Length: 0\r\n" +
                "Connection: close\r\n" +
                "\r\n");

            var authenticatedRequest = await AcceptAndRespondAsync(
                "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

            return (challengeRequest, authenticatedRequest);
        }

        public void Dispose()
        {
            _listener.Stop();
        }
    }

    private sealed class LoopbackSocks5ProxyServer : IDisposable
    {
        private readonly TcpListener _listener;

        public LoopbackSocks5ProxyServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();

            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            ProxyUri = new Uri($"socks5h://127.0.0.1:{endpoint.Port}/");
        }

        public Uri ProxyUri { get; }

        public async Task<Socks5ProxyResult> AcceptAndRespondAsync(string response)
        {
            using var client = await _listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            await using var stream = client.GetStream();

            await ReadSocks5GreetingAsync(stream);
            await stream.WriteAsync(new byte[] { 0x05, 0x02 }, TestContext.Current.CancellationToken);

            var (userName, password) = await ReadSocks5UserPasswordAsync(stream);
            await stream.WriteAsync(new byte[] { 0x01, 0x00 }, TestContext.Current.CancellationToken);

            var requestedHost = await ReadSocks5ConnectRequestAsync(stream);
            await stream.WriteAsync(
                new byte[] { 0x05, 0x00, 0x00, 0x01, 127, 0, 0, 1, 0, 0 },
                TestContext.Current.CancellationToken);

            var httpRequest = await ReadRequestHeadersAsync(stream);
            var responseBytes = Encoding.ASCII.GetBytes(response);
            await stream.WriteAsync(responseBytes, TestContext.Current.CancellationToken);

            return new(userName, password, requestedHost, httpRequest);
        }

        private static async Task ReadSocks5GreetingAsync(NetworkStream stream)
        {
            var header = await ReadExactAsync(stream, 2);
            Assert.Equal(0x05, header[0]);
            var methods = await ReadExactAsync(stream, header[1]);
            Assert.Contains((byte)0x02, methods);
        }

        private static async Task<(string UserName, string Password)> ReadSocks5UserPasswordAsync(
            NetworkStream stream)
        {
            var header = await ReadExactAsync(stream, 2);
            Assert.Equal(0x01, header[0]);

            var userNameBytes = await ReadExactAsync(stream, header[1]);
            var passwordLength = (await ReadExactAsync(stream, 1))[0];
            var passwordBytes = await ReadExactAsync(stream, passwordLength);

            return (
                Encoding.UTF8.GetString(userNameBytes),
                Encoding.UTF8.GetString(passwordBytes));
        }

        private static async Task<string> ReadSocks5ConnectRequestAsync(NetworkStream stream)
        {
            var header = await ReadExactAsync(stream, 4);
            Assert.Equal(0x05, header[0]);
            Assert.Equal(0x01, header[1]);

            string host = header[3] switch
            {
                0x01 => new IPAddress(await ReadExactAsync(stream, 4)).ToString(),
                0x03 => Encoding.ASCII.GetString(await ReadExactAsync(
                    stream,
                    (await ReadExactAsync(stream, 1))[0])),
                0x04 => new IPAddress(await ReadExactAsync(stream, 16)).ToString(),
                var addressType => throw new InvalidOperationException(
                    $"Unsupported SOCKS5 address type {addressType}.")
            };

            await ReadExactAsync(stream, 2);
            return host;
        }

        private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int length)
        {
            var buffer = new byte[length];
            var offset = 0;

            while (offset < length)
            {
                var read = await stream.ReadAsync(
                    buffer.AsMemory(offset, length - offset),
                    TestContext.Current.CancellationToken);
                if (read == 0)
                    throw new EndOfStreamException();

                offset += read;
            }

            return buffer;
        }

        public void Dispose()
        {
            _listener.Stop();
        }
    }

    private sealed class LoopbackOriginServer : IDisposable
    {
        private readonly TcpListener _listener;

        public LoopbackOriginServer()
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

        public void Dispose()
        {
            _listener.Stop();
        }
    }

    private static Uri GetUnusedLoopbackProxyUri()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        listener.Stop();

        return new Uri($"http://127.0.0.1:{endpoint.Port}/");
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

    private sealed record Socks5ProxyResult(
        string UserName,
        string Password,
        string RequestedHost,
        string HttpRequest);
}
