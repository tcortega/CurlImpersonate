using System.Net;
using System.Net.Sockets;
using System.Text;
using CurlImpersonate.Enums;
using Xunit;

namespace CurlImpersonate.Http.Tests;

public sealed class RedirectMetadataTests
{
    [Fact]
    public async Task SendAsync_FollowRedirects_SetsEffectiveUriAndRedirectCount()
    {
        using var server = new LoopbackRedirectServer();
        using var handler = new CurlHandler(new CurlHandlerOptions { FollowRedirects = true });
        using var client = new HttpClient(handler);

        var serverTask = server.AcceptRedirectThenFinalAsync();
        using var response = await client.GetAsync(server.StartUri, TestContext.Current.CancellationToken);
        await serverTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.TryGetEffectiveUri(out var effectiveUri));
        Assert.Equal(server.FinalUri, effectiveUri);
        Assert.True(response.TryGetRedirectCount(out var redirectCount));
        Assert.Equal(1, redirectCount);
    }

    [Fact]
    public async Task SendAsync_FollowRedirects_ExposesOnlyFinalResponseHeaders()
    {
        using var server = new LoopbackRedirectServer();
        using var handler = new CurlHandler(new CurlHandlerOptions { FollowRedirects = true });
        using var client = new HttpClient(handler);

        var serverTask = server.AcceptRedirectThenFinalAsync(
            "HTTP/1.1 302 Found\r\nLocation: /final\r\nX-Hop: redirect\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
            "HTTP/1.1 200 OK\r\nX-Hop: final\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var response = await client.GetAsync(server.StartUri, TestContext.Current.CancellationToken);
        await serverTask;

        Assert.True(response.Headers.TryGetValues("X-Hop", out var values));
        Assert.Equal(["final"], values);
    }

    [Fact]
    public async Task SendAsync_FollowRedirects_SendsCookieFromRedirectHop()
    {
        using var server = new LoopbackRedirectServer();
        using var handler = new CurlHandler(new CurlHandlerOptions { FollowRedirects = true });
        using var client = new HttpClient(handler);

        var serverTask = server.AcceptRedirectWithCookieThenFinalAsync();
        using var response = await client.GetAsync(server.StartUri, TestContext.Current.CancellationToken);
        var finalRequest = await serverTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Cookie: hop=one", finalRequest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_FollowRedirects_RewritesPost302ToGet()
    {
        using var server = new LoopbackRedirectServer();
        using var handler = new CurlHandler(new CurlHandlerOptions { FollowRedirects = true });
        using var client = new HttpClient(handler);
        using var content = new StringContent("payload", Encoding.UTF8, "text/plain");

        var serverTask = server.AcceptRedirectThenFinalRequestsAsync(
            "HTTP/1.1 302 Found\r\nLocation: /final\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
            "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var response = await client.PostAsync(server.StartUri, content, TestContext.Current.CancellationToken);
        var (_, finalRequest) = await serverTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("GET /final HTTP/1.1", finalRequest, StringComparison.Ordinal);
        Assert.DoesNotContain("payload", finalRequest, StringComparison.Ordinal);
        Assert.DoesNotContain("Content-Length:", finalRequest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_FollowRedirects_StripsSensitiveHeadersAcrossOrigins()
    {
        using var redirectServer = new LoopbackRedirectServer();
        using var finalServer = new LoopbackRedirectServer();
        using var handler = new CurlHandler(new CurlHandlerOptions { FollowRedirects = true });
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, redirectServer.StartUri);
        request.Headers.Authorization = new("Bearer", "secret");
        request.Headers.Add("Cookie", "session=secret");

        var redirectTask = redirectServer.AcceptRedirectToAsync(finalServer.FinalUri);
        var finalTask = finalServer.AcceptAndRespondAsync(
            "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        await redirectTask;
        var finalRequest = await finalTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Authorization:", finalRequest, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cookie:", finalRequest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_NegativeMaxRedirects_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CurlHandler(new CurlHandlerOptions { MaxRedirects = -1 }));
    }

    [Fact]
    public async Task SendAsync_FollowRedirects_ReturnsLastResponseWhenMaxRedirectsExceeded()
    {
        using var server = new LoopbackRedirectServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            FollowRedirects = true,
            MaxRedirects = 0
        });
        using var client = new HttpClient(handler);

        var serverTask = server.AcceptAndRespondAsync(
            "HTTP/1.1 302 Found\r\nLocation: /final\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var response = await client.GetAsync(server.StartUri, TestContext.Current.CancellationToken);
        await serverTask;

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/final", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task SendAsync_FollowRedirects_BlocksNonHttpRedirect()
    {
        using var server = new LoopbackRedirectServer();
        using var handler = new CurlHandler(new CurlHandlerOptions { FollowRedirects = true });
        using var client = new HttpClient(handler);

        var serverTask = server.AcceptNonHttpRedirectAsync();
        using var response = await client.GetAsync(server.StartUri, TestContext.Current.CancellationToken);
        await serverTask;

        // HttpClientHandler parity: the non-http target is not followed and
        // the 3xx response is returned to the caller.
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("file:///etc/passwd", response.Headers.Location?.OriginalString);
    }

    private sealed class LoopbackRedirectServer : IDisposable
    {
        private readonly TcpListener _listener;

        public LoopbackRedirectServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();

            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            StartUri = new Uri($"http://127.0.0.1:{endpoint.Port}/start");
            FinalUri = new Uri($"http://127.0.0.1:{endpoint.Port}/final");
        }

        public Uri StartUri { get; }

        public Uri FinalUri { get; }

        public async Task AcceptRedirectThenFinalAsync()
        {
            await AcceptRedirectThenFinalAsync(
                $"HTTP/1.1 302 Found\r\nLocation: {FinalUri.PathAndQuery}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n",
                "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        }

        public async Task AcceptRedirectThenFinalAsync(string redirectResponse, string finalResponse)
        {
            await AcceptAndRespondAsync(
                redirectResponse);

            await AcceptAndRespondAsync(
                finalResponse);
        }

        public async Task<string> AcceptRedirectWithCookieThenFinalAsync()
        {
            await AcceptAndRespondAsync(
                "HTTP/1.1 302 Found\r\nLocation: /final\r\nSet-Cookie: hop=one; Path=/\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

            return await AcceptAndRespondAsync(
                "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        }

        public async Task<(string FirstRequest, string FinalRequest)> AcceptRedirectThenFinalRequestsAsync(
            string redirectResponse,
            string finalResponse)
        {
            var firstRequest = await AcceptAndRespondAsync(redirectResponse);
            var finalRequest = await AcceptAndRespondAsync(finalResponse);
            return (firstRequest, finalRequest);
        }

        public async Task<string> AcceptRedirectToAsync(Uri uri)
        {
            return await AcceptAndRespondAsync(
                $"HTTP/1.1 302 Found\r\nLocation: {uri.AbsoluteUri}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        }

        public async Task AcceptNonHttpRedirectAsync()
        {
            await AcceptAndRespondAsync(
                "HTTP/1.1 302 Found\r\nLocation: file:///etc/passwd\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        }

        public async Task<string> AcceptAndRespondAsync(string response)
        {
            using var client = await _listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            await using var stream = client.GetStream();
            var request = await ReadRequestAsync(stream);
            var responseBytes = Encoding.ASCII.GetBytes(response);
            await stream.WriteAsync(responseBytes, TestContext.Current.CancellationToken);
            return request;
        }

        private static async Task<string> ReadRequestAsync(NetworkStream stream)
        {
            var buffer = new byte[4096];
            var received = new List<byte>();
            var headerEnd = -1;
            var contentLength = 0;

            while (true)
            {
                var read = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);
                if (read == 0)
                    break;

                received.AddRange(buffer.AsSpan(0, read).ToArray());
                if (headerEnd < 0)
                {
                    headerEnd = FindHeaderEnd(received);
                    if (headerEnd >= 0)
                    {
                        var headers = Encoding.ASCII.GetString(received.GetRange(0, headerEnd).ToArray());
                        contentLength = GetContentLength(headers);
                    }
                }

                if (headerEnd >= 0 && received.Count - headerEnd - 4 >= contentLength)
                    return Encoding.ASCII.GetString(received.ToArray());
            }

            return Encoding.ASCII.GetString(received.ToArray());
        }

        private static int FindHeaderEnd(List<byte> bytes)
        {
            for (var i = 0; i <= bytes.Count - 4; i++)
            {
                if (bytes[i] == '\r' &&
                    bytes[i + 1] == '\n' &&
                    bytes[i + 2] == '\r' &&
                    bytes[i + 3] == '\n')
                {
                    return i;
                }
            }

            return -1;
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

                if (int.TryParse(line[(colonIndex + 1)..].Trim(), out var length))
                    return length;
            }

            return 0;
        }

        public void Dispose()
        {
            _listener.Stop();
        }
    }
}
