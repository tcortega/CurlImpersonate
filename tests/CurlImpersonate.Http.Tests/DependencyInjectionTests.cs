using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CurlImpersonate.Http.Tests;

public sealed class DependencyInjectionTests
{
    [Fact]
    public async Task AddCurlImpersonateClient_CreatesClientBackedByCurlHandler()
    {
        using var server = new LoopbackHttpServer();
        var services = new ServiceCollection();
        services.AddCurlImpersonateClient("curl", options =>
            options.HeaderPolicy = BrowserHeaderPolicy.DisableBrowserHeaders);

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var client = factory.CreateClient("curl");

        var serverTask = server.AcceptAndRespondAsync(
            "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var response = await client.GetAsync(server.BaseUri, TestContext.Current.CancellationToken);
        await serverTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.TryGetEffectiveUri(out _));
    }

    [Fact]
    public async Task AddCurlImpersonate_ComposesWithDelegatingHandlers()
    {
        using var server = new LoopbackHttpServer();
        var services = new ServiceCollection();
        services.AddTransient<MarkerHandler>();
        services.AddHttpClient("curl")
            .AddHttpMessageHandler<MarkerHandler>()
            .AddCurlImpersonate(options =>
                options.HeaderPolicy = BrowserHeaderPolicy.DisableBrowserHeaders);

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var client = factory.CreateClient("curl");

        var serverTask = server.AcceptAndRespondAsync(
            "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var response = await client.GetAsync(server.BaseUri, TestContext.Current.CancellationToken);
        var request = await serverTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("X-Marker: middleware", request, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddCurlImpersonate_ReusesPrimaryHandlerAcrossClients()
    {
        var created = 0;
        var services = new ServiceCollection();
        services.AddCurlImpersonateClient("curl", _ => Interlocked.Increment(ref created));

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var first = factory.CreateClient("curl");
        using var second = factory.CreateClient("curl");

        Assert.Equal(1, created);
    }

    [Fact]
    public async Task AddCurlImpersonate_ServiceProviderOverload_ResolvesServices()
    {
        using var server = new LoopbackHttpServer();
        var services = new ServiceCollection();
        services.AddSingleton(new ProfileSetting(BrowserHeaderPolicy.DisableBrowserHeaders));
        services.AddHttpClient("curl").AddCurlImpersonate((provider, options) =>
            options.HeaderPolicy = provider.GetRequiredService<ProfileSetting>().HeaderPolicy);

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var client = factory.CreateClient("curl");

        var serverTask = server.AcceptAndRespondAsync(
            "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var response = await client.GetAsync(server.BaseUri, TestContext.Current.CancellationToken);
        var request = await serverTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("sec-ch-ua", request, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ProfileSetting(BrowserHeaderPolicy HeaderPolicy);

    private sealed class MarkerHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Add("X-Marker", "middleware");
            return base.SendAsync(request, cancellationToken);
        }
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
