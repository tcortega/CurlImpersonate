using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CurlImpersonate.Enums;
using CurlImpersonate.Native;
using Xunit;

namespace CurlImpersonate.Tests;

public class FingerprintTests(ITestOutputHelper output)
{
    private const string RunFingerprintTestsEnvironmentVariable = "CURLIMPERSONATE_RUN_FINGERPRINT_TESTS";
    private const string FingerprintUrl = "https://tls.browserleaks.com/json";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate nuint WriteCallbackDelegate(byte* ptr, nuint size, nuint nmemb, nint userdata);

    private static string PerformRequest(BrowserProfile? profile = null)
    {
        RequireFingerprintValidationEnabled();

        var curl = NativeMethods.EasyInit();
        Assert.NotEqual(0, curl);

        var urlPtr = Marshal.StringToHGlobalAnsi(FingerprintUrl);
        var responseData = new StringBuilder();

        unsafe
        {
            WriteCallbackDelegate writeCallback = (ptr, size, nmemb, userdata) =>
            {
                var totalSize = size * nmemb;
                var data = new byte[totalSize];
                Marshal.Copy((nint)ptr, data, 0, (int)totalSize);
                responseData.Append(Encoding.UTF8.GetString(data));
                return totalSize;
            };

            var callbackPtr = Marshal.GetFunctionPointerForDelegate(writeCallback);

            try
            {
                var result = NativeMethods.EasySetOptPointer(curl, CurlOption.Url, urlPtr);
                Assert.Equal(CurlCode.Ok, result);

                if (OperatingSystem.IsWindows())
                {
                    // The bundled BoringSSL build has no default CA bundle on
                    // Windows; raw handles must opt into the OS certificate store.
                    result = NativeMethods.EasySetOptLong(curl, CurlOption.SslOptions, (long)CurlSslOption.NativeCa);
                    Assert.Equal(CurlCode.Ok, result);
                }

                if (profile.HasValue)
                {
                    result = NativeMethods.EasyImpersonate(curl, profile.Value.ToTargetString(), 1);
                    Assert.Equal(CurlCode.Ok, result);
                }

                // Enable automatic decompression
                var acceptEncoding = Marshal.StringToHGlobalAnsi("");
                result = NativeMethods.EasySetOptPointer(curl, CurlOption.AcceptEncoding, acceptEncoding);
                Marshal.FreeHGlobal(acceptEncoding);
                Assert.Equal(CurlCode.Ok, result);

                result = NativeMethods.EasySetOptPointer(curl, CurlOption.WriteFunction, callbackPtr);
                Assert.Equal(CurlCode.Ok, result);

                result = NativeMethods.EasyPerform(curl);
                Assert.Equal(CurlCode.Ok, result);

                GC.KeepAlive(writeCallback);
                return responseData.ToString();
            }
            finally
            {
                Marshal.FreeHGlobal(urlPtr);
                NativeMethods.EasyCleanup(curl);
            }
        }
    }

    private static void RequireFingerprintValidationEnabled()
    {
        var value = Environment.GetEnvironmentVariable(RunFingerprintTestsEnvironmentVariable);
        if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Assert.Skip(
            $"Set {RunFingerprintTestsEnvironmentVariable}=1 to run external fingerprint validation against {FingerprintUrl}.");
    }

    [Fact]
    public void NotImpersonate_ShouldHaveDifferentFingerprintFromChrome()
    {
        var noImpersonateResponse = PerformRequest(profile: null);
        using var noImpersonateDoc = JsonDocument.Parse(noImpersonateResponse);
        var noImpersonateJa3 = noImpersonateDoc.RootElement.GetProperty("ja3_hash").GetString();
        output.WriteLine($"No impersonation JA3: {noImpersonateJa3}");

        var chromeResponse = PerformRequest(BrowserProfile.Chrome142);
        using var chromeDoc = JsonDocument.Parse(chromeResponse);
        var chromeJa3 = chromeDoc.RootElement.GetProperty("ja3_hash").GetString();
        output.WriteLine($"Chrome142 JA3: {chromeJa3}");

        Assert.NotEqual(chromeJa3, noImpersonateJa3);
    }

    [Fact]
    public void DifferentBrowserFamilies_ShouldHaveDifferentFingerprints()
    {
        var chromeResponse = PerformRequest(BrowserProfile.Chrome142);
        using var chromeDoc = JsonDocument.Parse(chromeResponse);
        var chromeJa3 = chromeDoc.RootElement.GetProperty("ja3_hash").GetString();
        output.WriteLine($"Chrome142 JA3: {chromeJa3}");

        var safariResponse = PerformRequest(BrowserProfile.Safari2601);
        using var safariDoc = JsonDocument.Parse(safariResponse);
        var safariJa3 = safariDoc.RootElement.GetProperty("ja3_hash").GetString();
        output.WriteLine($"Safari2601 JA3: {safariJa3}");

        var firefoxResponse = PerformRequest(BrowserProfile.Firefox144);
        using var firefoxDoc = JsonDocument.Parse(firefoxResponse);
        var firefoxJa3 = firefoxDoc.RootElement.GetProperty("ja3_hash").GetString();
        output.WriteLine($"Firefox144 JA3: {firefoxJa3}");

        Assert.NotEqual(chromeJa3, safariJa3);
        Assert.NotEqual(chromeJa3, firefoxJa3);
        Assert.NotEqual(safariJa3, firefoxJa3);
    }

    // Fingerprints captured using curl_cffi
    [Theory]
    [InlineData(BrowserProfile.Chrome101, "cd08e31494f9531f560d64c695473da9", "t13d1516h2_8daaf6152771_e5627efa2ab1", "4f04edce68a7ecbe689edce7bf5f23f3")]
    [InlineData(BrowserProfile.Safari2601, "ecdf4f49dd59effc439639da29186671", "t13d2013h2_a09f3c656075_7f0f34a4126d", "c52879e43202aeb92740be6e8c86ea96")]
    [InlineData(BrowserProfile.Firefox144, "6f7889b9fb1a62a9577e685c1fcfa919", "t13d1717h2_5b57614c22b0_3cbfd9057e0d", "6ea73faa8fc5aac76bded7bd238f6433")]
    [InlineData(BrowserProfile.Edge101, "cd08e31494f9531f560d64c695473da9", "t13d1516h2_8daaf6152771_e5627efa2ab1", "4f04edce68a7ecbe689edce7bf5f23f3")]
    public void BrowserProfile_ShouldMatchExpectedFingerprint(
        BrowserProfile profile,
        string expectedJa3,
        string expectedJa4,
        string expectedAkamai)
    {
        var response = PerformRequest(profile);
        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;

        var actualJa3 = root.GetProperty("ja3_hash").GetString();
        var actualJa4 = root.GetProperty("ja4").GetString();
        var actualAkamai = root.GetProperty("akamai_hash").GetString();

        output.WriteLine($"{profile}:");
        output.WriteLine($"  JA3:    expected={expectedJa3}, actual={actualJa3}");
        output.WriteLine($"  JA4:    expected={expectedJa4}, actual={actualJa4}");
        output.WriteLine($"  Akamai: expected={expectedAkamai}, actual={actualAkamai}");

        Assert.Equal(expectedJa3, actualJa3);
        Assert.Equal(expectedJa4, actualJa4);
        Assert.Equal(expectedAkamai, actualAkamai);
    }
}
