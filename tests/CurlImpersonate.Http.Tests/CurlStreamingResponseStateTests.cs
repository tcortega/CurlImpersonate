using System.Reflection;
using CurlImpersonate.Http.Internal;
using Xunit;

namespace CurlImpersonate.Http.Tests;

public sealed class CurlStreamingResponseStateTests
{
    [Fact]
    public void WriteBody_WhenByteCountWouldOverflow_Throws()
    {
        var state = new CurlStreamingResponseState(null!, null!, maxResponseBodyBytes: null);
        SetBytesWritten(state, long.MaxValue);

        var exception = Assert.Throws<InvalidOperationException>(
            () => state.WriteBody([1]));

        Assert.Equal("Streaming response body byte count is too large.", exception.Message);
    }

    [Fact]
    public void WriteBody_WhenQueueIsFull_SignalsPauseInsteadOfBlocking()
    {
        var state = new CurlStreamingResponseState(null!, null!, maxResponseBodyBytes: null);

        for (var i = 0; i < 16; i++)
            Assert.True(state.WriteBody([1]));

        Assert.False(state.WriteBody([1]));
    }

    [Fact]
    public async Task ReadChunkAsync_AfterPausedWrite_ResumesTransfer()
    {
        var state = new CurlStreamingResponseState(null!, null!, maxResponseBodyBytes: null);
        var resumed = 0;
        state.SetResumeTransfer(() => resumed++);

        for (var i = 0; i < 16; i++)
            Assert.True(state.WriteBody([1]));

        Assert.False(state.WriteBody([1]));

        Assert.NotNull(await state.ReadChunkAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, resumed);

        // The redelivered chunk fits now and later reads resume only once
        // per paused write.
        Assert.True(state.WriteBody([1]));
        Assert.NotNull(await state.ReadChunkAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, resumed);
    }

    private static void SetBytesWritten(CurlStreamingResponseState state, long bytesWritten)
    {
        var field = typeof(CurlStreamingResponseState).GetField(
            "_bytesWritten",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        field.SetValue(state, bytesWritten);
    }
}
