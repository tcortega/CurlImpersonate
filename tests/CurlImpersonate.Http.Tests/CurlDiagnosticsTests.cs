using CurlImpersonate.Http;
using Xunit;

namespace CurlImpersonate.Http.Tests;

public sealed class CurlDiagnosticsTests
{
    [Fact]
    public void Report_WithHandler_InvokesHandler()
    {
        var received = new List<string>();
        var previous = CurlDiagnostics.Handler;
        CurlDiagnostics.Handler = received.Add;
        try
        {
            CurlDiagnostics.Report("hello");
        }
        finally
        {
            CurlDiagnostics.Handler = previous;
        }

        Assert.Equal(new[] { "hello" }, received);
    }

    [Fact]
    public void Report_WithoutHandler_DoesNotThrow()
    {
        var previous = CurlDiagnostics.Handler;
        CurlDiagnostics.Handler = null;
        try
        {
            CurlDiagnostics.Report("fallback path");
        }
        finally
        {
            CurlDiagnostics.Handler = previous;
        }
    }
}
