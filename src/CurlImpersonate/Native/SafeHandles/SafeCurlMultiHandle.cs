using System.Runtime.InteropServices;

namespace CurlImpersonate.Native.SafeHandles;

/// <summary>
/// Safe handle wrapper for CURLM* (curl multi handle).
/// </summary>
public sealed class SafeCurlMultiHandle : SafeHandle
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SafeCurlMultiHandle"/> class.
    /// </summary>
    public SafeCurlMultiHandle() : base(0, ownsHandle: true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SafeCurlMultiHandle"/> class with a pre-existing handle.
    /// </summary>
    internal SafeCurlMultiHandle(nint handle, bool ownsHandle) : base(0, ownsHandle)
    {
        SetHandle(handle);
    }

    /// <inheritdoc/>
    public override bool IsInvalid => handle == 0;

    /// <inheritdoc/>
    protected override bool ReleaseHandle()
    {
        if (handle != 0)
        {
            NativeMethods.MultiCleanup(handle);
        }
        return true;
    }
}
