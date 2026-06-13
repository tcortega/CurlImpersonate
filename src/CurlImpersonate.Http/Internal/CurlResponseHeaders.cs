namespace CurlImpersonate.Http.Internal;

internal readonly record struct CurlResponseHeaders(
    int StatusCode,
    string[] Headers,
    Version? Version = null);
