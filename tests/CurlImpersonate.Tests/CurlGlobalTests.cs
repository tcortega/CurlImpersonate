using Xunit;

namespace CurlImpersonate.Tests;

public class CurlGlobalTests
{
    [Fact]
    public void Initialize_ShouldSucceed()
    {
        // Act - this will throw if libraries can't be loaded or init fails
        CurlGlobal.Initialize();

        // Assert
        Assert.True(CurlGlobal.IsInitialized);
    }

    [Fact]
    public void Version_ShouldReturnNonEmptyString()
    {
        // Arrange
        CurlGlobal.Initialize();

        // Act
        var version = CurlGlobal.Version;

        // Assert
        Assert.False(string.IsNullOrEmpty(version));
        Assert.Contains("libcurl", version);
    }
}
