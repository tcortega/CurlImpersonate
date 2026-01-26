using System.Runtime.InteropServices;
using CurlImpersonate.Enums;
using CurlImpersonate.Native;

namespace CurlImpersonate;

/// <summary>
/// Exception thrown when a curl operation fails.
/// </summary>
public class CurlException : Exception
{
    /// <summary>
    /// The curl error code.
    /// </summary>
    public CurlCode Code { get; }

    /// <summary>
    /// Creates a new CurlException with the specified error code.
    /// </summary>
    public CurlException(CurlCode code) : base(GetErrorMessage(code))
    {
        Code = code;
    }

    /// <summary>
    /// Creates a new CurlException with the specified error code and message.
    /// </summary>
    public CurlException(CurlCode code, string message) : base($"{message}: {GetErrorMessage(code)}")
    {
        Code = code;
    }

    /// <summary>
    /// Creates a new CurlException with the specified error code, message, and inner exception.
    /// </summary>
    public CurlException(CurlCode code, string message, Exception innerException)
        : base($"{message}: {GetErrorMessage(code)}", innerException)
    {
        Code = code;
    }

    /// <summary>
    /// Gets the error message for a curl error code.
    /// </summary>
    public static string GetErrorMessage(CurlCode code)
    {
        try
        {
            var ptr = NativeMethods.EasyStrError(code);
            if (ptr != 0)
            {
                return Marshal.PtrToStringAnsi(ptr) ?? code.ToString();
            }
        }
        catch
        {
            // Ignore errors getting the message
        }
        return code.ToString();
    }

    /// <summary>
    /// Throws a CurlException if the code indicates an error.
    /// </summary>
    public static void ThrowIfError(CurlCode code)
    {
        if (code != CurlCode.Ok)
        {
            throw new CurlException(code);
        }
    }

    /// <summary>
    /// Throws a CurlException if the code indicates an error.
    /// </summary>
    public static void ThrowIfError(CurlCode code, string operation)
    {
        if (code != CurlCode.Ok)
        {
            throw new CurlException(code, operation);
        }
    }
}

/// <summary>
/// Exception thrown when a curl multi operation fails.
/// </summary>
public class CurlMultiException : Exception
{
    /// <summary>
    /// The curl multi error code.
    /// </summary>
    public CurlMultiCode Code { get; }

    /// <summary>
    /// Creates a new CurlMultiException with the specified error code.
    /// </summary>
    public CurlMultiException(CurlMultiCode code) : base(GetErrorMessage(code))
    {
        Code = code;
    }

    /// <summary>
    /// Creates a new CurlMultiException with the specified error code and message.
    /// </summary>
    public CurlMultiException(CurlMultiCode code, string message) : base($"{message}: {GetErrorMessage(code)}")
    {
        Code = code;
    }

    /// <summary>
    /// Gets the error message for a curl multi error code.
    /// </summary>
    public static string GetErrorMessage(CurlMultiCode code)
    {
        try
        {
            var ptr = NativeMethods.MultiStrError(code);
            if (ptr != 0)
            {
                return Marshal.PtrToStringAnsi(ptr) ?? code.ToString();
            }
        }
        catch
        {
            // Ignore errors getting the message
        }
        return code.ToString();
    }

    /// <summary>
    /// Throws a CurlMultiException if the code indicates an error.
    /// </summary>
    public static void ThrowIfError(CurlMultiCode code)
    {
        if (code != CurlMultiCode.Ok)
        {
            throw new CurlMultiException(code);
        }
    }

    /// <summary>
    /// Throws a CurlMultiException if the code indicates an error.
    /// </summary>
    public static void ThrowIfError(CurlMultiCode code, string operation)
    {
        if (code != CurlMultiCode.Ok)
        {
            throw new CurlMultiException(code, operation);
        }
    }
}
