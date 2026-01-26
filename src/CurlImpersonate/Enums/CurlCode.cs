namespace CurlImpersonate.Enums;

/// <summary>
/// CURLcode return values from curl_easy_* functions.
/// </summary>
public enum CurlCode
{
    /// <summary>All fine.</summary>
    Ok = 0,
    /// <summary>Unsupported protocol.</summary>
    UnsupportedProtocol = 1,
    /// <summary>Failed initialization.</summary>
    FailedInit = 2,
    /// <summary>URL malformat.</summary>
    UrlMalformat = 3,
    /// <summary>Feature not built in.</summary>
    NotBuiltIn = 4,
    /// <summary>Couldn't resolve proxy.</summary>
    CouldntResolveProxy = 5,
    /// <summary>Couldn't resolve host.</summary>
    CouldntResolveHost = 6,
    /// <summary>Couldn't connect.</summary>
    CouldntConnect = 7,
    /// <summary>Weird server reply.</summary>
    WeirdServerReply = 8,
    /// <summary>Remote access denied.</summary>
    RemoteAccessDenied = 9,
    /// <summary>FTP accept failed.</summary>
    FtpAcceptFailed = 10,
    /// <summary>FTP weird PASS reply.</summary>
    FtpWeirdPassReply = 11,
    /// <summary>FTP accept timeout.</summary>
    FtpAcceptTimeout = 12,
    /// <summary>FTP weird PASV reply.</summary>
    FtpWeirdPasvReply = 13,
    /// <summary>FTP weird 227 format.</summary>
    FtpWeird227Format = 14,
    /// <summary>FTP can't get host.</summary>
    FtpCantGetHost = 15,
    /// <summary>HTTP/2 error.</summary>
    Http2 = 16,
    /// <summary>FTP couldn't set type.</summary>
    FtpCouldntSetType = 17,
    /// <summary>Partial file.</summary>
    PartialFile = 18,
    /// <summary>FTP couldn't retrieve file.</summary>
    FtpCouldntRetrFile = 19,
    /// <summary>Obsolete error.</summary>
    Obsolete20 = 20,
    /// <summary>Quote error.</summary>
    QuoteError = 21,
    /// <summary>HTTP returned error.</summary>
    HttpReturnedError = 22,
    /// <summary>Write error.</summary>
    WriteError = 23,
    /// <summary>Obsolete error.</summary>
    Obsolete24 = 24,
    /// <summary>Upload failed.</summary>
    UploadFailed = 25,
    /// <summary>Read error.</summary>
    ReadError = 26,
    /// <summary>Out of memory.</summary>
    OutOfMemory = 27,
    /// <summary>Operation timed out.</summary>
    OperationTimedOut = 28,
    /// <summary>Obsolete error.</summary>
    Obsolete29 = 29,
    /// <summary>FTP PORT failed.</summary>
    FtpPortFailed = 30,
    /// <summary>FTP couldn't use REST.</summary>
    FtpCouldntUseRest = 31,
    /// <summary>Obsolete error.</summary>
    Obsolete32 = 32,
    /// <summary>Range error.</summary>
    RangeError = 33,
    /// <summary>Obsolete error.</summary>
    Obsolete34 = 34,
    /// <summary>SSL connect error.</summary>
    SslConnectError = 35,
    /// <summary>Bad download resume.</summary>
    BadDownloadResume = 36,
    /// <summary>File couldn't read file.</summary>
    FileCouldntReadFile = 37,
    /// <summary>LDAP cannot bind.</summary>
    LdapCannotBind = 38,
    /// <summary>LDAP search failed.</summary>
    LdapSearchFailed = 39,
    /// <summary>Obsolete error.</summary>
    Obsolete40 = 40,
    /// <summary>Obsolete error.</summary>
    Obsolete41 = 41,
    /// <summary>Aborted by callback.</summary>
    AbortedByCallback = 42,
    /// <summary>Bad function argument.</summary>
    BadFunctionArgument = 43,
    /// <summary>Obsolete error.</summary>
    Obsolete44 = 44,
    /// <summary>Interface failed.</summary>
    InterfaceFailed = 45,
    /// <summary>Obsolete error.</summary>
    Obsolete46 = 46,
    /// <summary>Too many redirects.</summary>
    TooManyRedirects = 47,
    /// <summary>Unknown option.</summary>
    UnknownOption = 48,
    /// <summary>Setopt option syntax error.</summary>
    SetoptOptionSyntax = 49,
    /// <summary>Obsolete error.</summary>
    Obsolete50 = 50,
    /// <summary>Obsolete error.</summary>
    Obsolete51 = 51,
    /// <summary>Got nothing.</summary>
    GotNothing = 52,
    /// <summary>SSL engine not found.</summary>
    SslEngineNotFound = 53,
    /// <summary>SSL engine set failed.</summary>
    SslEngineSetFailed = 54,
    /// <summary>Send error.</summary>
    SendError = 55,
    /// <summary>Receive error.</summary>
    RecvError = 56,
    /// <summary>Obsolete error.</summary>
    Obsolete57 = 57,
    /// <summary>SSL certificate problem.</summary>
    SslCertProblem = 58,
    /// <summary>SSL cipher error.</summary>
    SslCipher = 59,
    /// <summary>Peer failed verification.</summary>
    PeerFailedVerification = 60,
    /// <summary>Bad content encoding.</summary>
    BadContentEncoding = 61,
    /// <summary>Obsolete error.</summary>
    Obsolete62 = 62,
    /// <summary>Filesize exceeded.</summary>
    FilesizeExceeded = 63,
    /// <summary>Use SSL failed.</summary>
    UseSslFailed = 64,
    /// <summary>Send fail rewind.</summary>
    SendFailRewind = 65,
    /// <summary>SSL engine init failed.</summary>
    SslEngineInitFailed = 66,
    /// <summary>Login denied.</summary>
    LoginDenied = 67,
    /// <summary>TFTP not found.</summary>
    TftpNotFound = 68,
    /// <summary>TFTP permission error.</summary>
    TftpPerm = 69,
    /// <summary>Remote disk full.</summary>
    RemoteDiskFull = 70,
    /// <summary>TFTP illegal.</summary>
    TftpIllegal = 71,
    /// <summary>TFTP unknown ID.</summary>
    TftpUnknownId = 72,
    /// <summary>Remote file exists.</summary>
    RemoteFileExists = 73,
    /// <summary>TFTP no such user.</summary>
    TftpNoSuchUser = 74,
    /// <summary>Obsolete error.</summary>
    Obsolete75 = 75,
    /// <summary>Obsolete error.</summary>
    Obsolete76 = 76,
    /// <summary>SSL CA cert bad file.</summary>
    SslCaCertBadFile = 77,
    /// <summary>Remote file not found.</summary>
    RemoteFileNotFound = 78,
    /// <summary>SSH error.</summary>
    Ssh = 79,
    /// <summary>SSL shutdown failed.</summary>
    SslShutdownFailed = 80,
    /// <summary>Socket not ready, try again.</summary>
    Again = 81,
    /// <summary>SSL CRL bad file.</summary>
    SslCrlBadFile = 82,
    /// <summary>SSL issuer error.</summary>
    SslIssuerError = 83,
    /// <summary>FTP PRET failed.</summary>
    FtpPretFailed = 84,
    /// <summary>RTSP CSEQ error.</summary>
    RtspCseqError = 85,
    /// <summary>RTSP session error.</summary>
    RtspSessionError = 86,
    /// <summary>FTP bad file list.</summary>
    FtpBadFileList = 87,
    /// <summary>Chunk failed.</summary>
    ChunkFailed = 88,
    /// <summary>No connection available.</summary>
    NoConnectionAvailable = 89,
    /// <summary>SSL pinned pubkey not match.</summary>
    SslPinnedPubkeyNotMatch = 90,
    /// <summary>SSL invalid cert status.</summary>
    SslInvalidCertStatus = 91,
    /// <summary>HTTP/2 stream error.</summary>
    Http2Stream = 92,
    /// <summary>Recursive API call.</summary>
    RecursiveApiCall = 93,
    /// <summary>Authentication error.</summary>
    AuthError = 94,
    /// <summary>HTTP/3 error.</summary>
    Http3 = 95,
    /// <summary>QUIC connect error.</summary>
    QuicConnectError = 96,
    /// <summary>Proxy error.</summary>
    Proxy = 97,
    /// <summary>SSL client cert error.</summary>
    SslClientCert = 98,
    /// <summary>Unrecoverable poll error.</summary>
    UnrecoverablePoll = 99,
    /// <summary>Response too large.</summary>
    TooLarge = 100,
    /// <summary>ECH required.</summary>
    EchRequired = 101,
}
