namespace CurlImpersonate.Enums;

/// <summary>
/// CURLINFO_* values for curl_easy_getinfo.
/// Type encoding: STRING=0x100000, LONG=0x200000, DOUBLE=0x300000, SLIST=0x400000, SOCKET=0x500000, OFF_T=0x600000
/// </summary>
public enum CurlInfo
{
    /// <summary>Debug text info type.</summary>
    Text = 0,
    /// <summary>Effective URL (string).</summary>
    EffectiveUrl = 0x100001,
    /// <summary>HTTP response code (long).</summary>
    ResponseCode = 0x200002,
    /// <summary>Total transfer time (double).</summary>
    TotalTime = 0x300003,
    /// <summary>Name lookup time (double).</summary>
    NameLookupTime = 0x300004,
    /// <summary>Connect time (double).</summary>
    ConnectTime = 0x300005,
    /// <summary>Pre-transfer time (double).</summary>
    PreTransferTime = 0x300006,
    /// <summary>Upload size (off_t).</summary>
    SizeUploadT = 0x600007,
    /// <summary>Download size (off_t).</summary>
    SizeDownloadT = 0x600008,
    /// <summary>Download speed (off_t).</summary>
    SpeedDownloadT = 0x600009,
    /// <summary>Upload speed (off_t).</summary>
    SpeedUploadT = 0x60000A,
    /// <summary>Header size (long).</summary>
    HeaderSize = 0x20000B,
    /// <summary>Request size (long).</summary>
    RequestSize = 0x20000C,
    /// <summary>SSL verify result (long).</summary>
    SslVerifyResult = 0x20000D,
    /// <summary>File time (long).</summary>
    FileTime = 0x20000E,
    /// <summary>File time (off_t).</summary>
    FileTimeT = 0x60000E,
    /// <summary>Content length download (off_t).</summary>
    ContentLengthDownloadT = 0x60000F,
    /// <summary>Content length upload (off_t).</summary>
    ContentLengthUploadT = 0x600010,
    /// <summary>Start transfer time (double).</summary>
    StartTransferTime = 0x300011,
    /// <summary>Content type (string).</summary>
    ContentType = 0x100012,
    /// <summary>Redirect time (double).</summary>
    RedirectTime = 0x300013,
    /// <summary>Redirect count (long).</summary>
    RedirectCount = 0x200014,
    /// <summary>Private data (string/pointer).</summary>
    Private = 0x100015,
    /// <summary>HTTP connect code (long).</summary>
    HttpConnectCode = 0x200016,
    /// <summary>HTTP auth available (long).</summary>
    HttpAuthAvail = 0x200017,
    /// <summary>Proxy auth available (long).</summary>
    ProxyAuthAvail = 0x200018,
    /// <summary>OS errno (long).</summary>
    OsErrno = 0x200019,
    /// <summary>Number of connects (long).</summary>
    NumConnects = 0x20001A,
    /// <summary>SSL engines (slist).</summary>
    SslEngines = 0x40001B,
    /// <summary>Cookie list (slist).</summary>
    CookieList = 0x40001C,
    /// <summary>FTP entry path (string).</summary>
    FtpEntryPath = 0x10001E,
    /// <summary>Redirect URL (string).</summary>
    RedirectUrl = 0x10001F,
    /// <summary>Primary IP (string).</summary>
    PrimaryIp = 0x100020,
    /// <summary>Application connect time (double).</summary>
    AppConnectTime = 0x300021,
    /// <summary>Certificate info (slist).</summary>
    CertInfo = 0x400022,
    /// <summary>Condition unmet (long).</summary>
    ConditionUnmet = 0x200023,
    /// <summary>RTSP session ID (string).</summary>
    RtspSessionId = 0x100024,
    /// <summary>RTSP client CSEQ (long).</summary>
    RtspClientCSeq = 0x200025,
    /// <summary>RTSP server CSEQ (long).</summary>
    RtspServerCSeq = 0x200026,
    /// <summary>RTSP CSEQ received (long).</summary>
    RtspCSeqRecv = 0x200027,
    /// <summary>Primary port (long).</summary>
    PrimaryPort = 0x200028,
    /// <summary>Local IP (string).</summary>
    LocalIp = 0x100029,
    /// <summary>Local port (long).</summary>
    LocalPort = 0x20002A,
    /// <summary>Active socket (socket).</summary>
    ActiveSocket = 0x50002C,
    /// <summary>TLS SSL pointer (slist).</summary>
    TlsSslPtr = 0x40002D,
    /// <summary>HTTP version (long).</summary>
    HttpVersion = 0x20002E,
    /// <summary>Proxy SSL verify result (long).</summary>
    ProxySslVerifyResult = 0x20002F,
    /// <summary>Protocol (long) - deprecated.</summary>
    Protocol = 0x200030,
    /// <summary>Scheme (string).</summary>
    Scheme = 0x100031,
    /// <summary>Total time (off_t, microseconds).</summary>
    TotalTimeT = 0x600032,
    /// <summary>Name lookup time (off_t, microseconds).</summary>
    NameLookupTimeT = 0x600033,
    /// <summary>Connect time (off_t, microseconds).</summary>
    ConnectTimeT = 0x600034,
    /// <summary>Pre-transfer time (off_t, microseconds).</summary>
    PreTransferTimeT = 0x600035,
    /// <summary>Start transfer time (off_t, microseconds).</summary>
    StartTransferTimeT = 0x600036,
    /// <summary>Redirect time (off_t, microseconds).</summary>
    RedirectTimeT = 0x600037,
    /// <summary>App connect time (off_t, microseconds).</summary>
    AppConnectTimeT = 0x600038,
    /// <summary>Retry-After value (off_t).</summary>
    RetryAfter = 0x600039,
    /// <summary>Effective method (string).</summary>
    EffectiveMethod = 0x10003A,
    /// <summary>Proxy error (long).</summary>
    ProxyError = 0x20003B,
    /// <summary>Referer (string).</summary>
    Referer = 0x10003C,
    /// <summary>CA info (string).</summary>
    CaInfo = 0x10003D,
    /// <summary>CA path (string).</summary>
    CaPath = 0x10003E,
    /// <summary>Transfer ID (off_t).</summary>
    XferId = 0x60003F,
    /// <summary>Connection ID (off_t).</summary>
    ConnId = 0x600040,
    /// <summary>Queue time (off_t, microseconds).</summary>
    QueueTimeT = 0x600041,
    /// <summary>Used proxy (long).</summary>
    UsedProxy = 0x200042,
    /// <summary>Post-transfer time (off_t, microseconds).</summary>
    PostTransferTimeT = 0x600043,
    /// <summary>Early data sent (off_t).</summary>
    EarlyDataSentT = 0x600044,
    /// <summary>HTTP auth used (long).</summary>
    HttpAuthUsed = 0x200045,
    /// <summary>Proxy auth used (long).</summary>
    ProxyAuthUsed = 0x200046,
}
