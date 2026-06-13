using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace CurlImpersonate.Http.Tests;

public sealed class MethodBodyTests
{
    [Fact]
    public async Task SendAsync_PostWithExplicitEmptyContent_SendsZeroLengthBody()
    {
        using var server = new LoopbackHttpServer();
        using var handler = new CurlHandler(new CurlHandlerOptions { MaxPoolSize = 1 });
        using var client = new HttpClient(handler);

        var requestTask = server.AcceptAndRespondAsync();
        using var content = new ByteArrayContent([]);
        using var response = await client.PostAsync(server.BaseUri, content, TestContext.Current.CancellationToken);
        var request = await requestTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("POST / HTTP/1.1", request.Headers, StringComparison.Ordinal);
        Assert.Contains("Content-Length: 0", request.Headers, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(request.Body);
    }

    [Fact]
    public async Task SendAsync_PutWithBody_SendsPutMethodAndBody()
    {
        using var server = new LoopbackHttpServer();
        using var handler = new CurlHandler(new CurlHandlerOptions { MaxPoolSize = 1 });
        using var client = new HttpClient(handler);

        var requestTask = server.AcceptAndRespondAsync();
        using var response = await client.PutAsync(
            server.BaseUri,
            new StringContent("put-body", Encoding.UTF8, "text/plain"),
            TestContext.Current.CancellationToken);
        var request = await requestTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("PUT / HTTP/1.1", request.Headers, StringComparison.Ordinal);
        Assert.Equal("put-body", request.Body);
    }

    [Fact]
    public async Task SendAsync_GetWithBody_SendsGetMethodAndBody()
    {
        using var server = new LoopbackHttpServer();
        using var handler = new CurlHandler(new CurlHandlerOptions { MaxPoolSize = 1 });
        using var client = new HttpClient(handler);
        using var requestMessage = new HttpRequestMessage(HttpMethod.Get, server.BaseUri)
        {
            Content = new StringContent("get-body", Encoding.UTF8, "text/plain")
        };

        var requestTask = server.AcceptAndRespondAsync();
        using var response = await client.SendAsync(requestMessage, TestContext.Current.CancellationToken);
        var request = await requestTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("GET / HTTP/1.1", request.Headers, StringComparison.Ordinal);
        Assert.Equal("get-body", request.Body);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("GET")]
    public async Task SendAsync_StreamRequestBodies_SendsBodyWithoutBuffering(string method)
    {
        using var server = new LoopbackHttpServer();
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            FollowRedirects = false,
            MaxPoolSize = 1,
            StreamRequestBodies = true
        });
        using var client = new HttpClient(handler);
        using var content = new StreamOnlyContent("stream-body");
        using var requestMessage = new HttpRequestMessage(new HttpMethod(method), server.BaseUri)
        {
            Content = content
        };

        var requestTask = server.AcceptAndRespondAsync();
        using var response = await client.SendAsync(requestMessage, TestContext.Current.CancellationToken);
        var request = await requestTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith($"{method} / HTTP/1.1", request.Headers, StringComparison.Ordinal);
        Assert.Equal("stream-body", request.Body);
        Assert.True(content.CreatedReadStream);
        Assert.False(content.Serialized);
    }

    [Fact]
    public async Task SendAsync_StreamRequestBodiesWithRedirects_ThrowsNotSupportedException()
    {
        using var handler = new CurlHandler(new CurlHandlerOptions { StreamRequestBodies = true });
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1/")
        {
            Content = new StreamOnlyContent("body")
        };

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => client.SendAsync(request, TestContext.Current.CancellationToken));

        Assert.Contains(nameof(CurlHandlerOptions.StreamRequestBodies), exception.Message);
        Assert.Contains(nameof(CurlHandlerOptions.FollowRedirects), exception.Message);
    }

    [Fact]
    public async Task SendAsync_StreamingRequestBodyExceedsKnownLimit_ThrowsInvalidOperationException()
    {
        using var handler = new CurlHandler(new CurlHandlerOptions
        {
            FollowRedirects = false,
            MaxRequestBodyBytes = 4,
            StreamRequestBodies = true
        });
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1/")
        {
            Content = new StreamOnlyContent("too-large")
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendAsync(request, TestContext.Current.CancellationToken));

        Assert.Contains("configured limit of 4 bytes", exception.Message);
    }

    [Fact]
    public async Task SendAsync_PostThenDelete_DoesNotLeakBodyState()
    {
        using var server = new LoopbackHttpServer();
        using var handler = new CurlHandler(new CurlHandlerOptions { MaxPoolSize = 1 });
        using var client = new HttpClient(handler);

        var postTask = server.AcceptAndRespondAsync();
        using (await client.PostAsync(
            server.BaseUri,
            new StringContent("post-body", Encoding.UTF8, "text/plain"),
            TestContext.Current.CancellationToken))
        {
        }
        var postRequest = await postTask;

        var deleteTask = server.AcceptAndRespondAsync();
        using (await client.DeleteAsync(server.BaseUri, TestContext.Current.CancellationToken))
        {
        }
        var deleteRequest = await deleteTask;

        Assert.StartsWith("POST / HTTP/1.1", postRequest.Headers, StringComparison.Ordinal);
        Assert.Equal("post-body", postRequest.Body);
        Assert.StartsWith("DELETE / HTTP/1.1", deleteRequest.Headers, StringComparison.Ordinal);
        Assert.DoesNotContain("Content-Length:", deleteRequest.Headers, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(deleteRequest.Body);
    }

    [Theory]
    [InlineData("PATCH")]
    [InlineData("OPTIONS")]
    [InlineData("PROPFIND")]
    public async Task SendAsync_PostThenCustomMethodWithoutBody_DoesNotLeakBodyState(string method)
    {
        using var server = new LoopbackHttpServer();
        using var handler = new CurlHandler(new CurlHandlerOptions { MaxPoolSize = 1 });
        using var client = new HttpClient(handler);

        var postTask = server.AcceptAndRespondAsync();
        using (await client.PostAsync(
            server.BaseUri,
            new StringContent("post-body", Encoding.UTF8, "text/plain"),
            TestContext.Current.CancellationToken))
        {
        }
        var postRequest = await postTask;

        var customTask = server.AcceptAndRespondAsync();
        using var customRequest = new HttpRequestMessage(new HttpMethod(method), server.BaseUri);
        using (await client.SendAsync(customRequest, TestContext.Current.CancellationToken))
        {
        }
        var customCaptured = await customTask;

        Assert.StartsWith("POST / HTTP/1.1", postRequest.Headers, StringComparison.Ordinal);
        Assert.Equal("post-body", postRequest.Body);
        Assert.StartsWith($"{method} / HTTP/1.1", customCaptured.Headers, StringComparison.Ordinal);
        Assert.DoesNotContain("Content-Length:", customCaptured.Headers, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(customCaptured.Body);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("OPTIONS")]
    [InlineData("GET")]
    public async Task SendAsync_BodyRequestThenGet_DoesNotLeakMethodOrBodyState(string method)
    {
        using var server = new LoopbackHttpServer();
        using var handler = new CurlHandler(new CurlHandlerOptions { MaxPoolSize = 1 });
        using var client = new HttpClient(handler);
        var body = $"{method.ToLowerInvariant()}-body";

        var firstTask = server.AcceptAndRespondAsync();
        using var firstRequest = new HttpRequestMessage(new HttpMethod(method), server.BaseUri)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };
        using (await client.SendAsync(firstRequest, TestContext.Current.CancellationToken))
        {
        }
        var firstCaptured = await firstTask;

        var getTask = server.AcceptAndRespondAsync();
        using (await client.GetAsync(server.BaseUri, TestContext.Current.CancellationToken))
        {
        }
        var getCaptured = await getTask;

        Assert.StartsWith($"{method} / HTTP/1.1", firstCaptured.Headers, StringComparison.Ordinal);
        Assert.Equal(body, firstCaptured.Body);
        Assert.StartsWith("GET / HTTP/1.1", getCaptured.Headers, StringComparison.Ordinal);
        Assert.DoesNotContain("Content-Length:", getCaptured.Headers, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(getCaptured.Body);
    }

    [Fact]
    public async Task SendAsync_HeadWithContent_ThrowsNotSupportedException()
    {
        using var handler = new CurlHandler();
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Head, "http://127.0.0.1/")
        {
            Content = new StringContent("body")
        };

        await Assert.ThrowsAsync<NotSupportedException>(
            () => client.SendAsync(request, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_HeadWithoutContent_SendsHeadAndReturnsEmptyBody()
    {
        using var server = new LoopbackHttpServer();
        using var handler = new CurlHandler(new CurlHandlerOptions { MaxPoolSize = 1 });
        using var client = new HttpClient(handler);
        using var requestMessage = new HttpRequestMessage(HttpMethod.Head, server.BaseUri);

        var requestTask = server.AcceptAndRespondAsync(
            "HTTP/1.1 200 OK\r\nContent-Length: 4\r\nConnection: close\r\n\r\nbody");
        using var response = await client.SendAsync(requestMessage, TestContext.Current.CancellationToken);
        var request = await requestTask;
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("HEAD / HTTP/1.1", request.Headers, StringComparison.Ordinal);
        Assert.Empty(request.Body);
        Assert.Empty(body);
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

        public async Task<CapturedRequest> AcceptAndRespondAsync(string? response = null)
        {
            using var client = await _listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            await using var stream = client.GetStream();

            var request = await ReadRequestAsync(stream);
            var responseBytes = Encoding.ASCII.GetBytes(response ??
                "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(responseBytes, TestContext.Current.CancellationToken);
            return request;
        }

        private static async Task<CapturedRequest> ReadRequestAsync(NetworkStream stream)
        {
            var buffer = new byte[4096];
            var received = new List<byte>();
            var headerEnd = -1;

            while (headerEnd < 0)
            {
                var read = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);
                if (read == 0)
                    break;

                received.AddRange(buffer.AsSpan(0, read).ToArray());
                headerEnd = FindHeaderEnd(received);
            }

            var headers = Encoding.ASCII.GetString(received.GetRange(0, headerEnd + 4).ToArray());
            var contentLength = GetContentLength(headers);
            var bodyStart = headerEnd + 4;

            while (received.Count - bodyStart < contentLength)
            {
                var read = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);
                if (read == 0)
                    break;

                received.AddRange(buffer.AsSpan(0, read).ToArray());
            }

            var bodyBytes = received
                .Skip(bodyStart)
                .Take(contentLength)
                .ToArray();

            return new CapturedRequest(headers, Encoding.UTF8.GetString(bodyBytes));
        }

        private static int FindHeaderEnd(List<byte> bytes)
        {
            for (var i = 3; i < bytes.Count; i++)
            {
                if (bytes[i - 3] == '\r' &&
                    bytes[i - 2] == '\n' &&
                    bytes[i - 1] == '\r' &&
                    bytes[i] == '\n')
                {
                    return i - 3;
                }
            }

            return -1;
        }

        private static int GetContentLength(string headers)
        {
            foreach (var line in headers.Split("\r\n"))
            {
                if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    continue;

                return int.Parse(line["Content-Length:".Length..].Trim());
            }

            return 0;
        }

        public void Dispose()
        {
            _listener.Stop();
        }
    }

    private sealed record CapturedRequest(string Headers, string Body);

    private sealed class StreamOnlyContent : HttpContent
    {
        private readonly byte[] _body;

        public StreamOnlyContent(string body)
        {
            _body = Encoding.UTF8.GetBytes(body);
        }

        public bool CreatedReadStream { get; private set; }

        public bool Serialized { get; private set; }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            CreatedReadStream = true;
            return Task.FromResult<Stream>(new MemoryStream(_body, writable: false));
        }

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
        {
            CreatedReadStream = true;
            return Task.FromResult<Stream>(new MemoryStream(_body, writable: false));
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            Serialized = true;
            throw new InvalidOperationException("Buffered serialization should not be used.");
        }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken)
        {
            Serialized = true;
            throw new InvalidOperationException("Buffered serialization should not be used.");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _body.Length;
            return true;
        }
    }
}
