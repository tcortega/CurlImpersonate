using CurlImpersonate.Enums;
using Xunit;

namespace CurlImpersonate.Tests;

public sealed class BrowserProfileTests
{
    [Theory]
    [InlineData(BrowserProfile.Chrome145, "chrome145")]
    [InlineData(BrowserProfile.Chrome146, "chrome146")]
    [InlineData(BrowserProfile.Firefox147, "firefox147")]
    [InlineData(BrowserProfile.Safari260Ios, "safari260_ios")]
    public void ToTargetString_MapsCurrentProfiles(BrowserProfile profile, string expected)
    {
        Assert.Equal(expected, profile.ToTargetString());
    }

    [Fact]
    public void ToTargetString_MapsEveryProfile()
    {
        foreach (var profile in Enum.GetValues<BrowserProfile>())
        {
            Assert.False(string.IsNullOrWhiteSpace(profile.ToTargetString()));
        }
    }

    [Fact]
    public void Defaults_UseCurrentStableProfiles()
    {
        Assert.Equal(BrowserProfile.Chrome146, BrowserProfileExtensions.DefaultChrome);
        Assert.Equal(BrowserProfile.Firefox147, BrowserProfileExtensions.DefaultFirefox);
        Assert.Equal(BrowserProfile.Safari260, BrowserProfileExtensions.DefaultSafari);
        Assert.Equal(BrowserProfile.Safari260Ios, BrowserProfileExtensions.DefaultSafariIos);
    }
}
