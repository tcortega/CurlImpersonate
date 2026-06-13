using System.Net;
using System.Net.Sockets;
using System.Text;
using CurlImpersonate.Enums;
using Xunit;

namespace CurlImpersonate.Http.Tests;

public class CurlHandlerTests
{
    [Fact]
    public void Constructor_CreatesHandler_NoException()
    {
        using var handler = new CurlHandler();
        Assert.NotNull(handler);
    }

    [Fact]
    public async Task SendAsync_SimpleGet_ReturnsSuccess()
    {
        using var server = new LoopbackHttpServer();
        using var handler = new CurlHandler();
        using var client = new HttpClient(handler);

        var serverTask = server.AcceptAndRespondAsync(LoopbackHttpServer.Response(200, "OK", "hello"));
        var response = await client.GetAsync(server.BaseUri, TestContext.Current.CancellationToken);
        await serverTask;

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(200, (int)response.StatusCode);
        Assert.True(response.TryGetTransferMetrics(out var metrics));
        Assert.NotNull(metrics);
        Assert.True(metrics.TotalTime >= TimeSpan.Zero);
        Assert.Equal(IPAddress.Loopback.ToString(), metrics.PrimaryIp);
        Assert.Equal(5, metrics.DownloadedBytes);
        Assert.Equal(0, metrics.UploadedBytes);
        Assert.True(metrics.DownloadSpeedBytesPerSecond >= 0);
        Assert.True(metrics.UploadSpeedBytesPerSecond >= 0);
    }

    [Fact]
    public async Task SendAsync_WithHeaders_HeadersAreSent()
    {
        using var server = new LoopbackHttpServer();
        using var handler = new CurlHandler();
        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("X-Custom-Header", "test-value");

        var serverTask = server.AcceptAndRespondAsync(LoopbackHttpServer.Response(200, "OK"));
        var response = await client.GetAsync(server.BaseUri, TestContext.Current.CancellationToken);
        var requestText = await serverTask;

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("X-Custom-Header: test-value", requestText);
    }

    [Fact]
    public async Task SendAsync_PostWithBody_BodyIsSent()
    {
        using var server = new LoopbackHttpServer();
        using var handler = new CurlHandler();
        using var client = new HttpClient(handler);
        var content = new StringContent("{\"key\":\"value\"}", Encoding.UTF8, "application/json");

        var serverTask = server.AcceptAndRespondAsync(LoopbackHttpServer.Response(200, "OK"));
        var response = await client.PostAsync(server.BaseUri, content, TestContext.Current.CancellationToken);
        var requestText = await serverTask;

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("POST / HTTP/1.1", requestText);
        Assert.Contains("\"key\":\"value\"", requestText);
    }

    [Fact]
    public async Task SendAsync_WithCustomOptions_UsesOptions()
    {
        using var server = new LoopbackHttpServer();
        var options = new CurlHandlerOptions
        {
            BrowserProfile = BrowserProfile.Firefox144,
            Timeout = TimeSpan.FromSeconds(30)
        };
        using var handler = new CurlHandler(options);
        using var client = new HttpClient(handler);

        var serverTask = server.AcceptAndRespondAsync(LoopbackHttpServer.Response(200, "OK"));
        var response = await client.GetAsync(server.BaseUri, TestContext.Current.CancellationToken);
        await serverTask;

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task SendAsync_ConcurrentRequests_AllSucceed()
    {
        const int RequestCount = 35;

        using var server = new LoopbackHttpServer();
        using var handler = new CurlHandler();
        using var client = new HttpClient(handler);
        var tasks = new List<Task<HttpResponseMessage>>();

        var serverTask = server.AcceptManyAndRespondAsync(RequestCount, LoopbackHttpServer.Response(200, "OK"));
        for (var i = 0; i < RequestCount; i++)
        {
            tasks.Add(client.GetAsync(new Uri(server.BaseUri, $"?i={i}"), TestContext.Current.CancellationToken));
        }
        var responses = await Task.WhenAll(tasks);
        await serverTask;

        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode));
    }

    [Fact]
    public async Task SendAsync_Cancellation_ThrowsOperationCanceledException()
    {
        using var handler = new CurlHandler();
        using var client = new HttpClient(handler);
        using var cts = new CancellationTokenSource();

        await cts.CancelAsync();

        // HttpClient wraps OperationCanceledException in TaskCanceledException.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetAsync("http://127.0.0.1/", cts.Token));
    }

    [Fact]
    public async Task SendAsync_FollowRedirects_RedirectIsFollowed()
    {
        using var server = new LoopbackHttpServer();
        var options = new CurlHandlerOptions { FollowRedirects = true };
        using var handler = new CurlHandler(options);
        using var client = new HttpClient(handler);

        var serverTask = server.AcceptRedirectThenFinalAsync();
        var response = await client.GetAsync(new Uri(server.BaseUri, "start"), TestContext.Current.CancellationToken);
        await serverTask;

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(200, (int)response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_NotFoundError_Returns404()
    {
        using var server = new LoopbackHttpServer();
        using var handler = new CurlHandler();
        using var client = new HttpClient(handler);

        var serverTask = server.AcceptAndRespondAsync(LoopbackHttpServer.Response(404, "Not Found"));
        var response = await client.GetAsync(server.BaseUri, TestContext.Current.CancellationToken);
        await serverTask;

        Assert.Equal(404, (int)response.StatusCode);
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

        public static string Response(int statusCode, string reasonPhrase, string body = "")
        {
            var contentLength = Encoding.UTF8.GetByteCount(body);
            return
                $"HTTP/1.1 {statusCode} {reasonPhrase}\r\n" +
                "Content-Type: text/plain\r\n" +
                $"Content-Length: {contentLength}\r\n" +
                "Connection: close\r\n" +
                "\r\n" +
                body;
        }

        public async Task<string> AcceptAndRespondAsync(string response)
        {
            var client = await _listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            return await RespondAsync(client, response);
        }

        public async Task<IReadOnlyList<string>> AcceptManyAndRespondAsync(int count, string response)
        {
            var tasks = new List<Task<string>>(count);
            for (var i = 0; i < count; i++)
            {
                var client = await _listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
                tasks.Add(RespondAsync(client, response));
            }

            return await Task.WhenAll(tasks);
        }

        public async Task AcceptRedirectThenFinalAsync()
        {
            await AcceptAndRespondAsync(
                "HTTP/1.1 302 Found\r\nLocation: /final\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

            await AcceptAndRespondAsync(Response(200, "OK"));
        }

        private static async Task<string> RespondAsync(TcpClient client, string response)
        {
            using (client)
            {
                await using var stream = client.GetStream();

                var request = await ReadRequestAsync(stream);
                var responseBytes = Encoding.UTF8.GetBytes(response);
                await stream.WriteAsync(responseBytes, TestContext.Current.CancellationToken);
                return request;
            }
        }

        private static async Task<string> ReadRequestAsync(NetworkStream stream)
        {
            var buffer = new byte[4096];
            var received = new List<byte>();

            while (true)
            {
                var read = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);
                if (read == 0)
                    break;

                received.AddRange(buffer.AsSpan(0, read).ToArray());
                var requestText = Encoding.UTF8.GetString(received.ToArray());
                var headerEnd = requestText.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (headerEnd < 0)
                    continue;

                var contentLength = GetContentLength(requestText[..headerEnd]);
                var bodyBytesRead = received.Count - headerEnd - 4;
                if (bodyBytesRead >= contentLength)
                    return requestText;
            }

            return Encoding.UTF8.GetString(received.ToArray());
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

                return int.TryParse(line[(colonIndex + 1)..].Trim(), out var length) ? length : 0;
            }

            return 0;
        }

        public void Dispose()
        {
            _listener.Stop();
        }
    }
}
