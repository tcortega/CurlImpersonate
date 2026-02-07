using CurlImpersonate.Enums;

namespace CurlImpersonate.Http;

/// <summary>
/// Exception thrown when a curl operation fails.
/// </summary>
public sealed class CurlException : Exception
{
    /// <summary>
    /// The curl error code.
    /// </summary>
    public CurlCode ErrorCode { get; }

    /// <summary>
    /// The error message from curl's error buffer, if available.
    /// </summary>
    public string? CurlErrorMessage { get; }

    /// <summary>
    /// Creates a new <see cref="CurlException"/> with the specified error code.
    /// </summary>
    public CurlException(CurlCode errorCode)
        : base($"Curl operation failed with error: {errorCode}")
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Creates a new <see cref="CurlException"/> with the specified error code and curl error message.
    /// </summary>
    public CurlException(CurlCode errorCode, string? curlErrorMessage)
        : base(string.IsNullOrEmpty(curlErrorMessage)
            ? $"Curl operation failed with error: {errorCode}"
            : $"Curl operation failed with error {errorCode}: {curlErrorMessage}")
    {
        ErrorCode = errorCode;
        CurlErrorMessage = curlErrorMessage;
    }

    /// <summary>
    /// Creates a new <see cref="CurlException"/> with the specified error code and inner exception.
    /// </summary>
    public CurlException(CurlCode errorCode, Exception innerException)
        : base($"Curl operation failed with error: {errorCode}", innerException)
    {
        ErrorCode = errorCode;
    }
}
