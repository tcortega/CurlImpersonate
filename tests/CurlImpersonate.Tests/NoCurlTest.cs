using Xunit;

namespace CurlImpersonate.Tests;

public class NoCurlTest
{
    [Fact]
    public void BasicTest_ShouldPass()
    {
        Assert.True(true);
    }
}
