using CurlImpersonate.Enums;

namespace CurlImpersonate.Http.Internal;

internal static class CurlNativeErrors
{
    public static void ThrowIfError(CurlCode code, string operation, string? curlErrorMessage = null)
    {
        if (code == CurlCode.Ok)
            return;

        throw new CurlException(code, operation, curlErrorMessage);
    }

    public static CurlException CreateException(
        CurlCode code,
        string operation,
        string? curlErrorMessage = null,
        Exception? innerException = null)
    {
        return innerException is null
            ? new CurlException(code, operation, curlErrorMessage)
            : new CurlException(code, operation, curlErrorMessage, innerException);
    }

    public static void ThrowIfMultiError(CurlMultiCode code, string operation)
    {
        if (code == CurlMultiCode.Ok)
            return;

        throw new CurlMultiException(code, operation);
    }

    public static CurlMultiException CreateMultiException(
        CurlMultiCode code,
        string operation)
    {
        return new CurlMultiException(code, operation);
    }

    public static HttpRequestException WrapTransportException(Exception exception)
    {
        var error = exception is CurlException curlException
            ? MapHttpRequestError(curlException.ErrorCode)
            : HttpRequestError.Unknown;

        return new HttpRequestException(error, exception.Message, exception);
    }

    public static HttpIOException WrapBodyReadException(Exception exception)
    {
        var error = exception is CurlException curlException
            ? MapHttpRequestError(curlException.ErrorCode)
            : HttpRequestError.Unknown;

        return new HttpIOException(error, exception.Message, exception);
    }

    private static HttpRequestError MapHttpRequestError(CurlCode code)
    {
        return code switch
        {
            CurlCode.CouldntResolveHost or CurlCode.CouldntResolveProxy => HttpRequestError.NameResolutionError,
            CurlCode.CouldntConnect => HttpRequestError.ConnectionError,
            CurlCode.SslConnectError or CurlCode.PeerFailedVerification or CurlCode.SslCertProblem
                => HttpRequestError.SecureConnectionError,
            CurlCode.Http2 or CurlCode.Http2Stream => HttpRequestError.HttpProtocolError,
            CurlCode.PartialFile or CurlCode.GotNothing => HttpRequestError.ResponseEnded,
            _ => HttpRequestError.Unknown
        };
    }
}
