using System.Runtime.InteropServices;

namespace CurlImpersonate.Native.SafeHandles;

/// <summary>
/// Safe handle wrapper for CURL* (curl easy handle).
/// </summary>
public sealed class SafeCurlHandle : SafeHandle
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SafeCurlHandle"/> class.
    /// </summary>
    public SafeCurlHandle() : base(0, ownsHandle: true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SafeCurlHandle"/> class with a pre-existing handle.
    /// </summary>
    internal SafeCurlHandle(nint handle, bool ownsHandle) : base(0, ownsHandle)
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
            NativeMethods.EasyCleanup(handle);
        }
        return true;
    }
}
