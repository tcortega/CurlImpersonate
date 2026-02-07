using CurlImpersonate.Enums;
using Xunit;

namespace CurlImpersonate.Http.Tests;

public class CurlHandlerTests
{
    [Fact]
    public void Constructor_CreatesHandler_NoException()
    {
        // Minimal test - just create and dispose
        using var handler = new CurlHandler();
        Assert.NotNull(handler);
    }

    [Fact]
    public async Task SendAsync_SimpleGet_ReturnsSuccess()
    {
        // Arrange
        using var handler = new CurlHandler();
        using var client = new HttpClient(handler);

        // Act
        var response = await client.GetAsync("https://httpbin.org/get", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(200, (int)response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_WithHeaders_HeadersAreSent()
    {
        // Arrange
        using var handler = new CurlHandler();
        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("X-Custom-Header", "test-value");

        // Act
        var response = await client.GetAsync("https://httpbin.org/headers", TestContext.Current.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("X-Custom-Header", content);
        Assert.Contains("test-value", content);
    }

    [Fact]
    public async Task SendAsync_PostWithBody_BodyIsSent()
    {
        // Arrange
        using var handler = new CurlHandler();
        using var client = new HttpClient(handler);
        var content = new StringContent("{\"key\":\"value\"}", System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("https://httpbin.org/post", content, TestContext.Current.CancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("\"key\"", responseContent);
        Assert.Contains("\"value\"", responseContent);
    }

    [Fact]
    public async Task SendAsync_WithCustomOptions_UsesOptions()
    {
        // Arrange
        var options = new CurlHandlerOptions
        {
            BrowserProfile = BrowserProfile.Firefox144,
            Timeout = TimeSpan.FromSeconds(30)
        };
        using var handler = new CurlHandler(options);
        using var client = new HttpClient(handler);

        // Act
        var response = await client.GetAsync("https://httpbin.org/get", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task SendAsync_ConcurrentRequests_AllSucceed()
    {
        // Arrange
        using var handler = new CurlHandler();
        using var client = new HttpClient(handler);
        var tasks = new List<Task<HttpResponseMessage>>();

        // Act - Send 10 concurrent requests
        for (var i = 0; i < 35; i++)
        {
            tasks.Add(client.GetAsync($"https://httpbin.org/get?i={i}", TestContext.Current.CancellationToken));
        }
        var responses = await Task.WhenAll(tasks);

        // Assert
        Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode));
    }

    [Fact]
    public async Task SendAsync_Cancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        using var handler = new CurlHandler();
        using var client = new HttpClient(handler);
        using var cts = new CancellationTokenSource();

        // Cancel immediately
        await cts.CancelAsync();

        // Act & Assert - HttpClient wraps OperationCanceledException in TaskCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetAsync("https://httpbin.org/delay/5", cts.Token));
    }

    [Fact]
    public async Task SendAsync_FollowRedirects_RedirectIsFollowed()
    {
        // Arrange
        var options = new CurlHandlerOptions { FollowRedirects = true };
        using var handler = new CurlHandler(options);
        using var client = new HttpClient(handler);

        // Act - httpbin.org/redirect/1 redirects once to /get
        var response = await client.GetAsync("https://httpbin.org/redirect/1", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(200, (int)response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_NotFoundError_Returns404()
    {
        // Arrange
        using var handler = new CurlHandler();
        using var client = new HttpClient(handler);

        // Act
        var response = await client.GetAsync("https://httpbin.org/status/404", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(404, (int)response.StatusCode);
    }
}
