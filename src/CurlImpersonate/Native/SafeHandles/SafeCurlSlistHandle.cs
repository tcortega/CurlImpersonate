using System.Runtime.InteropServices;

namespace CurlImpersonate.Native.SafeHandles;

/// <summary>
/// Safe handle wrapper for curl_slist* (curl string list).
/// </summary>
public sealed class SafeCurlSlistHandle : SafeHandle
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SafeCurlSlistHandle"/> class.
    /// </summary>
    public SafeCurlSlistHandle() : base(0, ownsHandle: true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SafeCurlSlistHandle"/> class with a pre-existing handle.
    /// </summary>
    internal SafeCurlSlistHandle(nint handle, bool ownsHandle) : base(0, ownsHandle)
    {
        SetHandle(handle);
    }

    /// <inheritdoc/>
    public override bool IsInvalid => handle == 0;

    /// <summary>
    /// Gets the native handle value.
    /// </summary>
    public nint Handle => handle;

    /// <summary>
    /// Appends a string to this slist.
    /// </summary>
    /// <param name="data">The string to append.</param>
    public void Append(string data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var newHandle = NativeMethods.SlistAppend(handle, data);
        if (newHandle == 0)
            throw new OutOfMemoryException("curl_slist_append failed.");

        SetHandle(newHandle);
    }

    /// <inheritdoc/>
    protected override bool ReleaseHandle()
    {
        if (handle != 0)
        {
            NativeMethods.SlistFreeAll(handle);
        }
        return true;
    }
}
