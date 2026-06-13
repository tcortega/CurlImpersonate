using System.Net;
using System.Net.Sockets;
using System.Text;
using CurlImpersonate.Enums;
using Xunit;

namespace CurlImpersonate.Http.Tests;

public sealed class BufferLimitTests
{
    [Fact]
    public async Task SendAsync_RequestBodyExceedsLimit_ThrowsInvalidOperationException()
    {
        using var handler = new CurlHandler(new CurlHandlerOptions { MaxRequestBodyBytes = 4 });
        using var client = new HttpClient(handler);
        using var content = new ByteArrayContent(Encoding.ASCII.GetBytes("too-large"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.PostAsync("http://127.0.0.1/", content, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_UnknownLengthRequestBodyExceedsLimit_StopsReadingAtLimit()
    {
        using var handler = new CurlHandler(new CurlHandlerOptions { MaxRequestBodyBytes = 4 });
        using var client = new HttpClient(handler);
        using var content = new UnknownLengthContent("too-large");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.PostAsync("http://127.0.0.1/", content, TestContext.Current.CancellationToken));

        Assert.Equal(5, content.BytesRead);
    }

    [Fact]
    public async Task SendAsync_ResponseBodyExceedsLimit_ThrowsHttpRequestException()
    {
        using var server = new LoopbackHttpServer();
        using var handler = new CurlHandler(new CurlHandlerOptions { MaxResponseBodyBytes = 4 });
        using var client = new HttpClient(handler);

        var serverTask = server.AcceptAndRespondAsync("too-large");

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync(server.BaseUri, TestContext.Current.CancellationToken));
        await serverTask;

        var curlException = Assert.IsType<CurlException>(exception.InnerException);
        Assert.Equal(CurlCode.WriteError, curlException.ErrorCode);
        Assert.IsType<InvalidOperationException>(curlException.InnerException);
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

        public async Task AcceptAndRespondAsync(string body)
        {
            using var client = await _listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            await using var stream = client.GetStream();
            await ReadRequestHeadersAsync(stream);

            var bodyBytes = Encoding.ASCII.GetBytes(body);
            var headerBytes = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headerBytes, TestContext.Current.CancellationToken);
            await stream.WriteAsync(bodyBytes, TestContext.Current.CancellationToken);
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

    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] _body;

        public UnknownLengthContent(string body)
        {
            _body = Encoding.ASCII.GetBytes(body);
        }

        public int BytesRead { get; private set; }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            return Task.FromResult<Stream>(new CountingReadStream(_body, bytesRead => BytesRead += bytesRead));
        }

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<Stream>(new CountingReadStream(_body, bytesRead => BytesRead += bytesRead));
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            throw new InvalidOperationException("Buffered serialization should not be used.");
        }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Buffered serialization should not be used.");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class CountingReadStream : Stream
    {
        private readonly byte[] _data;
        private readonly Action<int> _onRead;
        private int _position;

        public CountingReadStream(byte[] data, Action<int> onRead)
        {
            _data = data;
            _onRead = onRead;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            if (_position >= _data.Length || buffer.Length == 0)
                return 0;

            var bytesToRead = Math.Min(buffer.Length, _data.Length - _position);
            _data.AsSpan(_position, bytesToRead).CopyTo(buffer);
            _position += bytesToRead;
            _onRead(bytesToRead);
            return bytesToRead;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
