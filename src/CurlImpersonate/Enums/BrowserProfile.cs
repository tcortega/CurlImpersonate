namespace CurlImpersonate.Enums;

/// <summary>
/// Browser profiles for curl_easy_impersonate.
/// </summary>
public enum BrowserProfile
{
    // Chrome
    /// <summary>Chrome 99</summary>
    Chrome99,
    /// <summary>Chrome 100</summary>
    Chrome100,
    /// <summary>Chrome 101</summary>
    Chrome101,
    /// <summary>Chrome 104</summary>
    Chrome104,
    /// <summary>Chrome 107</summary>
    Chrome107,
    /// <summary>Chrome 110</summary>
    Chrome110,
    /// <summary>Chrome 116</summary>
    Chrome116,
    /// <summary>Chrome 119</summary>
    Chrome119,
    /// <summary>Chrome 120</summary>
    Chrome120,
    /// <summary>Chrome 123</summary>
    Chrome123,
    /// <summary>Chrome 124</summary>
    Chrome124,
    /// <summary>Chrome 131</summary>
    Chrome131,
    /// <summary>Chrome 133a</summary>
    Chrome133a,
    /// <summary>Chrome 136</summary>
    Chrome136,
    /// <summary>Chrome 142 (latest)</summary>
    Chrome142,
    /// <summary>Chrome 99 Android</summary>
    Chrome99Android,
    /// <summary>Chrome 131 Android</summary>
    Chrome131Android,

    // Edge
    /// <summary>Edge 99</summary>
    Edge99,
    /// <summary>Edge 101</summary>
    Edge101,

    // Safari
    /// <summary>Safari 15.3</summary>
    Safari153,
    /// <summary>Safari 15.5</summary>
    Safari155,
    /// <summary>Safari 17.0</summary>
    Safari170,
    /// <summary>Safari 17.2 iOS</summary>
    Safari172Ios,
    /// <summary>Safari 18.0</summary>
    Safari180,
    /// <summary>Safari 18.0 iOS</summary>
    Safari180Ios,
    /// <summary>Safari 18.4</summary>
    Safari184,
    /// <summary>Safari 18.4 iOS</summary>
    Safari184Ios,
    /// <summary>Safari 26.0</summary>
    Safari260,
    /// <summary>Safari 26.0.1 (latest)</summary>
    Safari2601,
    /// <summary>Safari 26.0 iOS</summary>
    Safari260Ios,

    // Firefox
    /// <summary>Firefox 133</summary>
    Firefox133,
    /// <summary>Firefox 135</summary>
    Firefox135,
    /// <summary>Firefox 144 (latest)</summary>
    Firefox144,

    // Tor
    /// <summary>Tor Browser 145</summary>
    Tor145,
}

/// <summary>
/// Extension methods for <see cref="BrowserProfile"/>.
/// </summary>
public static class BrowserProfileExtensions
{
    /// <summary>
    /// Converts the browser profile to the target string used by curl_easy_impersonate.
    /// </summary>
    public static string ToTargetString(this BrowserProfile profile) => profile switch
    {
        // Chrome
        BrowserProfile.Chrome99 => "chrome99",
        BrowserProfile.Chrome100 => "chrome100",
        BrowserProfile.Chrome101 => "chrome101",
        BrowserProfile.Chrome104 => "chrome104",
        BrowserProfile.Chrome107 => "chrome107",
        BrowserProfile.Chrome110 => "chrome110",
        BrowserProfile.Chrome116 => "chrome116",
        BrowserProfile.Chrome119 => "chrome119",
        BrowserProfile.Chrome120 => "chrome120",
        BrowserProfile.Chrome123 => "chrome123",
        BrowserProfile.Chrome124 => "chrome124",
        BrowserProfile.Chrome131 => "chrome131",
        BrowserProfile.Chrome133a => "chrome133a",
        BrowserProfile.Chrome136 => "chrome136",
        BrowserProfile.Chrome142 => "chrome142",
        BrowserProfile.Chrome99Android => "chrome99_android",
        BrowserProfile.Chrome131Android => "chrome131_android",
        // Edge
        BrowserProfile.Edge99 => "edge99",
        BrowserProfile.Edge101 => "edge101",
        // Safari
        BrowserProfile.Safari153 => "safari153",
        BrowserProfile.Safari155 => "safari155",
        BrowserProfile.Safari170 => "safari170",
        BrowserProfile.Safari172Ios => "safari172_ios",
        BrowserProfile.Safari180 => "safari180",
        BrowserProfile.Safari180Ios => "safari180_ios",
        BrowserProfile.Safari184 => "safari184",
        BrowserProfile.Safari184Ios => "safari184_ios",
        BrowserProfile.Safari260 => "safari260",
        BrowserProfile.Safari2601 => "safari2601",
        BrowserProfile.Safari260Ios => "safari260_ios",
        // Firefox
        BrowserProfile.Firefox133 => "firefox133",
        BrowserProfile.Firefox135 => "firefox135",
        BrowserProfile.Firefox144 => "firefox144",
        // Tor
        BrowserProfile.Tor145 => "tor145",
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown browser profile")
    };

    /// <summary>
    /// Default Chrome profile.
    /// </summary>
    public static BrowserProfile DefaultChrome => BrowserProfile.Chrome142;

    /// <summary>
    /// Default Edge profile.
    /// </summary>
    public static BrowserProfile DefaultEdge => BrowserProfile.Edge101;

    /// <summary>
    /// Default Safari profile.
    /// </summary>
    public static BrowserProfile DefaultSafari => BrowserProfile.Safari2601;

    /// <summary>
    /// Default Safari iOS profile.
    /// </summary>
    public static BrowserProfile DefaultSafariIos => BrowserProfile.Safari260Ios;

    /// <summary>
    /// Default Firefox profile.
    /// </summary>
    public static BrowserProfile DefaultFirefox => BrowserProfile.Firefox144;

    /// <summary>
    /// Default Tor profile.
    /// </summary>
    public static BrowserProfile DefaultTor => BrowserProfile.Tor145;
}
