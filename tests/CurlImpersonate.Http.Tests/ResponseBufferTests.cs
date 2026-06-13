using CurlImpersonate.Http.Internal;
using System.Reflection;
using Xunit;

namespace CurlImpersonate.Http.Tests;

public sealed class ResponseBufferTests
{
    [Fact]
    public void Reset_ReleasesOversizedBuffer()
    {
        using var buffer = new ResponseBuffer(initialCapacity: 16, maxRetainedCapacity: 64);
        buffer.Write(new byte[1024]);

        Assert.True(buffer.Capacity > 64);

        buffer.Reset();

        Assert.Equal(0, buffer.Length);
        Assert.True(buffer.Capacity <= 64);
    }

    [Fact]
    public void Write_WhenBufferedLengthWouldOverflow_Throws()
    {
        using var buffer = new ResponseBuffer(initialCapacity: 16, maxRetainedCapacity: 64);
        SetPosition(buffer, Array.MaxLength);

        var exception = Assert.Throws<InvalidOperationException>(
            () => buffer.Write([1]));

        Assert.Equal("Response body is too large to buffer.", exception.Message);
    }

    private static void SetPosition(ResponseBuffer buffer, int position)
    {
        var field = typeof(ResponseBuffer).GetField(
            "_position",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        field.SetValue(buffer, position);
    }
}
