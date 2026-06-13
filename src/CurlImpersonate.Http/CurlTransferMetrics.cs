namespace CurlImpersonate.Http;

/// <summary>
/// Transfer metrics reported by libcurl for a completed response.
/// </summary>
public sealed record CurlTransferMetrics(
    TimeSpan TotalTime,
    TimeSpan NameLookupTime,
    TimeSpan ConnectTime,
    TimeSpan TlsHandshakeTime,
    TimeSpan PreTransferTime,
    TimeSpan StartTransferTime,
    TimeSpan RedirectTime,
    int NewConnectionCount,
    long RequestHeaderBytes,
    long ResponseHeaderBytes,
    string? PrimaryIp,
    int? PrimaryPort,
    string? LocalIp,
    int? LocalPort)
{
    /// <summary>
    /// Number of response body bytes downloaded for the transfer.
    /// </summary>
    public long DownloadedBytes { get; init; }

    /// <summary>
    /// Number of request body bytes uploaded for the transfer.
    /// </summary>
    public long UploadedBytes { get; init; }

    /// <summary>
    /// Average response body download speed in bytes per second.
    /// </summary>
    public long DownloadSpeedBytesPerSecond { get; init; }

    /// <summary>
    /// Average request body upload speed in bytes per second.
    /// </summary>
    public long UploadSpeedBytesPerSecond { get; init; }
}
