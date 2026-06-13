using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Xunit;

namespace CurlImpersonate.Http.Tests;

public sealed class CertificateTrustTests
{
    [Fact]
    public async Task SendAsync_DefaultCertificateVerification_RejectsUntrustedLocalCertificate()
    {
        using var server = new LoopbackHttpsServer();
        using var handler = new CurlHandler(new() { HeaderPolicy = BrowserHeaderPolicy.DisableBrowserHeaders });
        using var client = new HttpClient(handler);

        var serverTask = server.AcceptAndRespondAsync();
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync(server.BaseUri, TestContext.Current.CancellationToken));

        Assert.IsType<CurlException>(exception.InnerException);
        Assert.False(await serverTask);
    }

    [Fact]
    public async Task SendAsync_WithCaInfo_TrustsConfiguredCertificateAuthority()
    {
        using var server = new LoopbackHttpsServer();
        var debugLog = new StringBuilder();
        using var handler = new CurlHandler(new()
        {
            HeaderPolicy = BrowserHeaderPolicy.DisableBrowserHeaders,
            CaInfo = server.CertificateAuthorityPath,
            EnableCurlDebug = true,
            DebugCallback = e =>
            {
                if (e.Type == CurlDebugInfoType.Text)
                    lock (debugLog) debugLog.Append(e.GetText());
            }
        });
        using var client = new HttpClient(handler);

        var serverTask = server.AcceptAndRespondAsync();
        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(server.BaseUri, TestContext.Current.CancellationToken);
        }
        catch (HttpRequestException ex)
        {
            string log;
            lock (debugLog) log = debugLog.ToString();
            var serverError = "server handshake still pending";
            try
            {
                await serverTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                serverError = server.HandshakeException?.ToString() ?? "server reported no handshake error";
            }
            catch (TimeoutException)
            {
            }

            throw new InvalidOperationException(
                $"request failed: {ex.Message}\nserver error: {serverError}\ncurl debug log:\n{log}", ex);
        }

        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(await serverTask);
        }
    }

    private sealed class LoopbackHttpsServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly X509Certificate2 _serverCertificate;

        public LoopbackHttpsServer()
        {
            var certs = CreateCertificates();
            _serverCertificate = certs.ServerCertificate;
            CertificateAuthorityPath = Path.Combine(Path.GetTempPath(), $"curlimpersonate-ca-{Guid.NewGuid():N}.pem");
            WritePemCertificate(CertificateAuthorityPath, certs.CertificateAuthority);

            _listener = new(IPAddress.Loopback, 0);
            _listener.Start();

            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            BaseUri = new($"https://127.0.0.1:{endpoint.Port}/");
        }

        public Uri BaseUri { get; }

        public string CertificateAuthorityPath { get; }

        public Exception? HandshakeException { get; private set; }

        public async Task<bool> AcceptAndRespondAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            await using var networkStream = client.GetStream();
            await using var sslStream = new SslStream(networkStream, false);

            try
            {
                await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = _serverCertificate,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                }, TestContext.Current.CancellationToken);
            }
            catch (Exception ex) when (ex is AuthenticationException or IOException)
            {
                HandshakeException = ex;
                return false;
            }

            await ReadRequestHeadersAsync(sslStream);
            var responseBytes = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await sslStream.WriteAsync(responseBytes, TestContext.Current.CancellationToken);
            return true;
        }

        private static async Task ReadRequestHeadersAsync(Stream stream)
        {
            var buffer = new byte[4096];
            var received = new StringBuilder();

            while (true)
            {
                var read = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);
                if (read == 0)
                    return;

                received.Append(Encoding.ASCII.GetString(buffer, 0, read));
                if (received.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                    return;
            }
        }

        private static (X509Certificate2 CertificateAuthority, X509Certificate2 ServerCertificate) CreateCertificates()
        {
            using var rootKey = RSA.Create(2048);
            var rootRequest = new CertificateRequest(
                "CN=CurlImpersonate Test Root",
                rootKey,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                true));
            rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey, false));

            var now = DateTimeOffset.UtcNow;
            var rootCertificate = rootRequest.CreateSelfSigned(now.AddDays(-1), now.AddDays(7));

            using var serverKey = RSA.Create(2048);
            var serverRequest = new CertificateRequest(
                "CN=127.0.0.1",
                serverKey,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
            subjectAlternativeNames.AddIpAddress(IPAddress.Loopback);
            serverRequest.CertificateExtensions.Add(subjectAlternativeNames.Build());
            serverRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            serverRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                true));
            serverRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                [new Oid("1.3.6.1.5.5.7.3.1")],
                true));

            var serial = RandomNumberGenerator.GetBytes(16);
            using var ephemeralServerCertificate = serverRequest
                .Create(rootCertificate, now.AddDays(-1), now.AddDays(7), serial)
                .CopyWithPrivateKey(serverKey);

            // Schannel rejects ephemeral private keys for server authentication,
            // so round-trip through PKCS#12 to persist the key in a usable keyset.
            var serverCertificate = X509CertificateLoader.LoadPkcs12(
                ephemeralServerCertificate.Export(X509ContentType.Pfx),
                null);

            return (rootCertificate, serverCertificate);
        }

        private static void WritePemCertificate(string path, X509Certificate2 certificate)
        {
            var builder = new StringBuilder();
            builder.AppendLine("-----BEGIN CERTIFICATE-----");
            builder.AppendLine(Convert.ToBase64String(
                certificate.RawData,
                Base64FormattingOptions.InsertLineBreaks));
            builder.AppendLine("-----END CERTIFICATE-----");
            File.WriteAllText(path, builder.ToString());
        }

        public void Dispose()
        {
            _listener.Stop();
            _serverCertificate.Dispose();
            if (File.Exists(CertificateAuthorityPath))
                File.Delete(CertificateAuthorityPath);
        }
    }
}
