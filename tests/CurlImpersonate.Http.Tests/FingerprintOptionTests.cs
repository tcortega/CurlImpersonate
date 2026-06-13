using System.Net;
using System.Net.Sockets;
using System.Text;
using CurlImpersonate.Http.Internal;
using Xunit;

namespace CurlImpersonate.Http.Tests;

public sealed class FingerprintOptionTests
{
    private const string ChromeLikeJa3 =
        "771,4865-4866-4867-49195-49199-49196-49200-52393-52392-49171-49172-156-157-47-53,0-23-65281-10-11-35-16-5-13-18-51-45-43-27-17513-21,29-23-24,0";

    [Fact]
    public void ParseJa3Fingerprint_MapsSupportedFields()
    {
        var fingerprint = RequestMapper.ParseJa3Fingerprint(ChromeLikeJa3);

        Assert.Equal(65542, fingerprint.SslVersion);
        Assert.Equal(
            "TLS_AES_128_GCM_SHA256:TLS_AES_256_GCM_SHA384:TLS_CHACHA20_POLY1305_SHA256:" +
            "TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256:TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256:" +
            "TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384:TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384:" +
            "TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256:TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256:" +
            "TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA:TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA:" +
            "TLS_RSA_WITH_AES_128_GCM_SHA256:TLS_RSA_WITH_AES_256_GCM_SHA384:" +
            "TLS_RSA_WITH_AES_128_CBC_SHA:TLS_RSA_WITH_AES_256_CBC_SHA",
            fingerprint.CipherList);
        Assert.Equal(
            "0-23-65281-10-11-35-16-5-13-18-51-45-43-27-17513",
            fingerprint.ExtensionOrder);
        Assert.Equal("X25519:P-256:P-384", fingerprint.CurveList);
        Assert.Contains(27, fingerprint.ExtensionIds);
        Assert.Contains(17513, fingerprint.ExtensionIds);
    }

    [Fact]
    public void ParseJa3Fingerprint_PermuteExtensions_SkipsExtensionOrder()
    {
        var fingerprint = RequestMapper.ParseJa3Fingerprint(ChromeLikeJa3, permuteExtensions: true);

        Assert.Null(fingerprint.ExtensionOrder);
    }

    [Fact]
    public void ParseJa3Fingerprint_InvalidFormat_Throws()
    {
        Assert.Throws<ArgumentException>(() => RequestMapper.ParseJa3Fingerprint("771,4865,0-23"));
    }

    [Fact]
    public void ParseJa3Fingerprint_RejectsUnsupportedTlsVersion()
    {
        Assert.Throws<ArgumentException>(
            () => RequestMapper.ParseJa3Fingerprint(
                "772,4865,0-23-65281-10-11-35-16-13-51-45-43,29,0"));
    }

    [Fact]
    public void ParseJa3Fingerprint_RejectsUnsupportedCipher()
    {
        Assert.Throws<ArgumentException>(
            () => RequestMapper.ParseJa3Fingerprint(
                "771,999999,0-23-65281-10-11-35-16-13-51-45-43,29,0"));
    }

    [Fact]
    public void ParseJa3Fingerprint_RejectsUnsupportedCurveFormat()
    {
        Assert.Throws<ArgumentException>(
            () => RequestMapper.ParseJa3Fingerprint(
                "771,4865,0-23-65281-10-11-35-16-13-51-45-43,29,1"));
    }

    [Fact]
    public void ParseJa3Fingerprint_RejectsUnsupportedExtensionToggle()
    {
        Assert.Throws<ArgumentException>(
            () => RequestMapper.ParseJa3Fingerprint(
                "771,4865,23-65281-10-11-35-16-13-51-45-43,29,0"));
    }

    [Fact]
    public void ParseAkamaiFingerprint_NormalizesCurlCffiFormat()
    {
        var fingerprint = RequestMapper.ParseAkamaiFingerprint("1:65536,4:6291456|15663105|3:0:0:201|m,a,s,p");

        Assert.Equal("1:65536;4:6291456", fingerprint.Settings);
        Assert.Equal(15663105, fingerprint.WindowUpdate);
        Assert.Equal("3:0:0:201", fingerprint.Streams);
        Assert.Equal("masp", fingerprint.PseudoHeaderOrder);
    }

    [Fact]
    public void ParseAkamaiFingerprint_InvalidFormat_Throws()
    {
        Assert.Throws<ArgumentException>(() => RequestMapper.ParseAkamaiFingerprint("1:65536|missing"));
        Assert.Throws<ArgumentException>(() => RequestMapper.ParseAkamaiFingerprint("1:65536|NaN|0|masp"));
        Assert.Throws<ArgumentException>(() => RequestMapper.ParseAkamaiFingerprint("1:65536|-1|0|masp"));
    }

    [Fact]
    public void ParseHttp3Fingerprint_NormalizesPseudoHeaderOrder()
    {
        var fingerprint = RequestMapper.ParseHttp3Fingerprint("1:1;6:65536|m,a,s,p|1:1");

        Assert.Equal("1:1;6:65536", fingerprint.Settings);
        Assert.Equal("masp", fingerprint.PseudoHeaderOrder);
        Assert.Equal("1:1", fingerprint.QuicTransportParameters);
    }

    [Fact]
    public void ParseHttp3Fingerprint_InvalidFormat_Throws()
    {
        Assert.Throws<ArgumentException>(() => RequestMapper.ParseHttp3Fingerprint("settings|masp"));
    }

    [Fact]
    public async Task SendAsync_WithFingerprintOverrides_ConfiguresRequest()
    {
        using var server = new LoopbackHttpServer();
        var options = new CurlHandlerOptions
        {
            HeaderPolicy = BrowserHeaderPolicy.DisableBrowserHeaders
        };
        options.Fingerprint.Http2PseudoHeaderOrder = "masp";
        options.Fingerprint.Http2Settings = "1:65536;4:6291456;6:262144";
        options.Fingerprint.Http2WindowUpdate = 15663105;
        options.Fingerprint.Http2NoPriority = true;
        options.Fingerprint.Http2StreamWeight = 32;
        options.Fingerprint.TlsGrease = true;
        options.Fingerprint.FormBoundary = "webkit";
        options.Fingerprint.Http3PseudoHeaderOrder = "masp";
        options.Fingerprint.Http3Settings = "1:1;6:65536;7:1";
        options.Fingerprint.QuicTransportParameters = "1:1";
        options.Fingerprint.Http3SignatureHashAlgorithms = "rsa_pss_rsae_sha256";
        options.Fingerprint.Http3TlsExtensionOrder = "0-10";
        options.Fingerprint.Ja3 = ChromeLikeJa3;

        using var handler = new CurlHandler(options);
        using var client = new HttpClient(handler);

        var requestTask = server.AcceptAndRespondAsync();
        using var response = await client.GetAsync(server.BaseUri, TestContext.Current.CancellationToken);
        await requestTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_InvalidHttp2StreamWeight_ThrowsArgumentOutOfRangeException()
    {
        var options = new CurlHandlerOptions();
        options.Fingerprint.Http2StreamWeight = 0;

        using var handler = new CurlHandler(options);
        using var client = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.GetAsync("http://127.0.0.1/", TestContext.Current.CancellationToken));

        Assert.Equal(nameof(CurlFingerprintOptions.Http2StreamWeight), exception.ParamName);
    }

    [Theory]
    [InlineData(nameof(CurlFingerprintOptions.Http2WindowUpdate))]
    [InlineData(nameof(CurlFingerprintOptions.TlsRecordSizeLimit))]
    [InlineData(nameof(CurlFingerprintOptions.TlsKeySharesLimit))]
    public async Task SendAsync_NegativeFingerprintLong_ThrowsArgumentOutOfRangeException(
        string propertyName)
    {
        var options = new CurlHandlerOptions();
        switch (propertyName)
        {
            case nameof(CurlFingerprintOptions.Http2WindowUpdate):
                options.Fingerprint.Http2WindowUpdate = -1;
                break;
            case nameof(CurlFingerprintOptions.TlsRecordSizeLimit):
                options.Fingerprint.TlsRecordSizeLimit = -1;
                break;
            case nameof(CurlFingerprintOptions.TlsKeySharesLimit):
                options.Fingerprint.TlsKeySharesLimit = -1;
                break;
        }

        using var handler = new CurlHandler(options);
        using var client = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.GetAsync("http://127.0.0.1/", TestContext.Current.CancellationToken));

        Assert.Equal(propertyName, exception.ParamName);
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
