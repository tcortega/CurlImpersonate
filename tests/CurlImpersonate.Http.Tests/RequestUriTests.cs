using Xunit;

namespace CurlImpersonate.Http.Tests;

public sealed class RequestUriTests
{
    [Fact]
    public async Task SendAsync_RelativeRequestUri_ThrowsInvalidOperationException()
    {
        using var handler = new CurlHandler();
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/relative");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => invoker.SendAsync(request, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendAsync_NonHttpRequestUri_ThrowsNotSupportedException()
    {
        using var handler = new CurlHandler();
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "ftp://example.test/");

        await Assert.ThrowsAsync<NotSupportedException>(
            () => invoker.SendAsync(request, TestContext.Current.CancellationToken));
    }
}
