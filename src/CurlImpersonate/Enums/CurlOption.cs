namespace CurlImpersonate.Enums;

/// <summary>
/// CURLOPT_* options for curl_easy_setopt.
/// Option value encoding: LONG=0+N, OBJECTPOINT=10000+N, FUNCTIONPOINT=20000+N, OFF_T=30000+N, BLOB=40000+N
/// </summary>
public enum CurlOption
{
    // Basic options
    /// <summary>File to write to.</summary>
    WriteData = 10001,
    /// <summary>URL to fetch.</summary>
    Url = 10002,
    /// <summary>Port number to connect to.</summary>
    Port = 3,
    /// <summary>Proxy to use.</summary>
    Proxy = 10004,
    /// <summary>Username:password for the connection.</summary>
    UserPwd = 10005,
    /// <summary>Username:password for the proxy.</summary>
    ProxyUserPwd = 10006,
    /// <summary>Range to request.</summary>
    Range = 10007,
    /// <summary>File to read from.</summary>
    ReadData = 10009,
    /// <summary>Error buffer.</summary>
    ErrorBuffer = 10010,
    /// <summary>Write callback function.</summary>
    WriteFunction = 20000 + 11,
    /// <summary>Read callback function.</summary>
    ReadFunction = 20012,
    /// <summary>Timeout in seconds.</summary>
    Timeout = 13,
    /// <summary>Size of input file.</summary>
    InFileSize = 14,
    /// <summary>POST data.</summary>
    PostFields = 10015,
    /// <summary>Referer header.</summary>
    Referer = 10016,
    /// <summary>FTP port string.</summary>
    FtpPort = 10017,
    /// <summary>User-Agent header.</summary>
    UserAgent = 10018,
    /// <summary>Low speed limit.</summary>
    LowSpeedLimit = 19,
    /// <summary>Low speed time.</summary>
    LowSpeedTime = 20,
    /// <summary>Resume from offset.</summary>
    ResumeFrom = 21,
    /// <summary>Cookie string.</summary>
    Cookie = 10022,
    /// <summary>HTTP headers.</summary>
    HttpHeader = 10023,
    /// <summary>HTTP POST data (obsolete).</summary>
    HttpPost = 10024,
    /// <summary>SSL certificate.</summary>
    SslCert = 10025,
    /// <summary>SSL key password.</summary>
    KeyPasswd = 10026,
    /// <summary>CRLF conversion.</summary>
    Crlf = 27,
    /// <summary>FTP quote commands.</summary>
    Quote = 10028,
    /// <summary>Header callback data.</summary>
    HeaderData = 10029,
    /// <summary>Cookie file to read.</summary>
    CookieFile = 10031,
    /// <summary>SSL version to use.</summary>
    SslVersion = 32,
    /// <summary>Time condition.</summary>
    TimeCondition = 33,
    /// <summary>Time value.</summary>
    TimeValue = 34,
    /// <summary>Custom request method.</summary>
    CustomRequest = 10036,
    /// <summary>Stderr replacement.</summary>
    StdErr = 10037,
    /// <summary>FTP post-transfer commands.</summary>
    PostQuote = 10039,
    /// <summary>Verbose output.</summary>
    Verbose = 41,
    /// <summary>Include headers in output.</summary>
    Header = 42,
    /// <summary>Disable progress meter.</summary>
    NoProgress = 43,
    /// <summary>Don't get body.</summary>
    Nobody = 44,
    /// <summary>Fail on HTTP error.</summary>
    FailOnError = 45,
    /// <summary>Upload mode.</summary>
    Upload = 46,
    /// <summary>POST request.</summary>
    Post = 47,
    /// <summary>Directory listing only.</summary>
    DirListOnly = 48,
    /// <summary>Append mode.</summary>
    Append = 50,
    /// <summary>Use .netrc file.</summary>
    Netrc = 51,
    /// <summary>Follow redirects.</summary>
    FollowLocation = 52,
    /// <summary>Transfer as text.</summary>
    TransferText = 53,
    /// <summary>PUT request.</summary>
    Put = 54,
    /// <summary>Progress callback (deprecated).</summary>
    ProgressFunction = 20056,
    /// <summary>Progress callback data.</summary>
    XferInfoData = 10057,
    /// <summary>Auto-set Referer on redirect.</summary>
    AutoReferer = 58,
    /// <summary>Proxy port.</summary>
    ProxyPort = 59,
    /// <summary>POST field size.</summary>
    PostFieldSize = 60,
    /// <summary>Tunnel through proxy.</summary>
    HttpProxyTunnel = 61,
    /// <summary>Network interface to use.</summary>
    Interface = 10062,
    /// <summary>Kerberos level.</summary>
    KrbLevel = 10063,
    /// <summary>Verify SSL peer.</summary>
    SslVerifyPeer = 64,
    /// <summary>CA certificate info.</summary>
    CaInfo = 10065,
    /// <summary>Maximum redirects.</summary>
    MaxRedirs = 68,
    /// <summary>Request file time.</summary>
    FileTime = 69,
    /// <summary>Telnet options.</summary>
    TelnetOptions = 10070,
    /// <summary>Maximum connections.</summary>
    MaxConnects = 71,
    /// <summary>Force fresh connection.</summary>
    FreshConnect = 74,
    /// <summary>Forbid connection reuse.</summary>
    ForbidReuse = 75,
    /// <summary>Random file for SSL.</summary>
    RandomFile = 10076,
    /// <summary>EGD socket for SSL.</summary>
    EgdSocket = 10077,
    /// <summary>Connect timeout in seconds.</summary>
    ConnectTimeout = 78,
    /// <summary>Header callback function.</summary>
    HeaderFunction = 20079,
    /// <summary>GET request.</summary>
    HttpGet = 80,
    /// <summary>Verify SSL host.</summary>
    SslVerifyHost = 81,
    /// <summary>Cookie jar file.</summary>
    CookieJar = 10082,
    /// <summary>SSL cipher list.</summary>
    SslCipherList = 10083,
    /// <summary>HTTP version.</summary>
    HttpVersion = 84,
    /// <summary>Use FTP EPSV.</summary>
    FtpUseEpsv = 85,
    /// <summary>SSL certificate type.</summary>
    SslCertType = 10086,
    /// <summary>SSL private key.</summary>
    SslKey = 10087,
    /// <summary>SSL key type.</summary>
    SslKeyType = 10088,
    /// <summary>SSL engine.</summary>
    SslEngine = 10089,
    /// <summary>Default SSL engine.</summary>
    SslEngineDefault = 90,
    /// <summary>DNS global cache (deprecated).</summary>
    DnsUseGlobalCache = 91,
    /// <summary>DNS cache timeout.</summary>
    DnsCacheTimeout = 92,
    /// <summary>FTP pre-transfer commands.</summary>
    PreQuote = 10093,
    /// <summary>Debug callback function.</summary>
    DebugFunction = 20094,
    /// <summary>Debug callback data.</summary>
    DebugData = 10095,
    /// <summary>Cookie session.</summary>
    CookieSession = 96,
    /// <summary>CA path.</summary>
    CaPath = 10097,
    /// <summary>Buffer size.</summary>
    BufferSize = 98,
    /// <summary>Disable signals.</summary>
    NoSignal = 99,
    /// <summary>Share handle.</summary>
    Share = 10100,
    /// <summary>Proxy type.</summary>
    ProxyType = 101,
    /// <summary>Accept-Encoding header.</summary>
    AcceptEncoding = 10102,
    /// <summary>Private data pointer.</summary>
    Private = 10103,
    /// <summary>HTTP 200 aliases.</summary>
    Http200Aliases = 10104,
    /// <summary>Unrestricted auth.</summary>
    UnrestrictedAuth = 105,
    /// <summary>Use FTP EPRT.</summary>
    FtpUseEprt = 106,
    /// <summary>HTTP authentication methods.</summary>
    HttpAuth = 107,
    /// <summary>SSL context callback.</summary>
    SslCtxFunction = 20108,
    /// <summary>SSL context data.</summary>
    SslCtxData = 10109,
    /// <summary>Create missing FTP dirs.</summary>
    FtpCreateMissingDirs = 110,
    /// <summary>Proxy authentication methods.</summary>
    ProxyAuth = 111,
    /// <summary>Server response timeout.</summary>
    ServerResponseTimeout = 112,
    /// <summary>IP resolve preference.</summary>
    IpResolve = 113,
    /// <summary>Maximum file size.</summary>
    MaxFileSize = 114,
    /// <summary>Large input file size.</summary>
    InFileSizeLarge = 30115,
    /// <summary>Large resume from offset.</summary>
    ResumeFromLarge = 30116,
    /// <summary>Large maximum file size.</summary>
    MaxFileSizeLarge = 30117,
    /// <summary>.netrc file.</summary>
    NetrcFile = 10118,
    /// <summary>Use SSL.</summary>
    UseSsl = 119,
    /// <summary>Large POST field size.</summary>
    PostFieldSizeLarge = 30120,
    /// <summary>TCP nodelay.</summary>
    TcpNoDelay = 121,
    /// <summary>FTP SSL auth.</summary>
    FtpSslAuth = 129,
    /// <summary>IOCTL callback.</summary>
    IoctlFunction = 20130,
    /// <summary>IOCTL data.</summary>
    IoctlData = 10131,
    /// <summary>FTP account.</summary>
    FtpAccount = 10134,
    /// <summary>Cookie list.</summary>
    CookieList = 10135,
    /// <summary>Ignore content length.</summary>
    IgnoreContentLength = 136,
    /// <summary>Skip PASV IP.</summary>
    FtpSkipPasvIp = 137,
    /// <summary>FTP file method.</summary>
    FtpFileMethod = 138,
    /// <summary>Local port.</summary>
    LocalPort = 139,
    /// <summary>Local port range.</summary>
    LocalPortRange = 140,
    /// <summary>Connect only.</summary>
    ConnectOnly = 141,
    /// <summary>Max send speed.</summary>
    MaxSendSpeedLarge = 30145,
    /// <summary>Max receive speed.</summary>
    MaxRecvSpeedLarge = 30146,
    /// <summary>FTP alternative to USER.</summary>
    FtpAlternativeToUser = 10147,
    /// <summary>Socket option callback.</summary>
    SockOptFunction = 20148,
    /// <summary>Socket option data.</summary>
    SockOptData = 10149,
    /// <summary>SSL session ID cache.</summary>
    SslSessionIdCache = 150,
    /// <summary>SSH auth types.</summary>
    SshAuthTypes = 151,
    /// <summary>SSH public key file.</summary>
    SshPublicKeyFile = 10152,
    /// <summary>SSH private key file.</summary>
    SshPrivateKeyFile = 10153,
    /// <summary>FTP SSL CCC.</summary>
    FtpSslCcc = 154,
    /// <summary>Timeout in milliseconds.</summary>
    TimeoutMs = 155,
    /// <summary>Connect timeout in milliseconds.</summary>
    ConnectTimeoutMs = 156,
    /// <summary>HTTP transfer decoding.</summary>
    HttpTransferDecoding = 157,
    /// <summary>HTTP content decoding.</summary>
    HttpContentDecoding = 158,
    /// <summary>New file permissions.</summary>
    NewFilePerms = 159,
    /// <summary>New directory permissions.</summary>
    NewDirectoryPerms = 160,
    /// <summary>POST redirect behavior.</summary>
    PostRedir = 161,
    /// <summary>SSH host public key MD5.</summary>
    SshHostPublicKeyMd5 = 10162,
    /// <summary>Open socket callback.</summary>
    OpenSocketFunction = 20163,
    /// <summary>Open socket data.</summary>
    OpenSocketData = 10164,
    /// <summary>Copy POST fields.</summary>
    CopyPostFields = 10165,
    /// <summary>Proxy transfer mode.</summary>
    ProxyTransferMode = 166,
    /// <summary>Seek callback.</summary>
    SeekFunction = 20167,
    /// <summary>Seek data.</summary>
    SeekData = 10168,
    /// <summary>CRL file.</summary>
    CrlFile = 10169,
    /// <summary>Issuer certificate.</summary>
    IssuerCert = 10170,
    /// <summary>Address scope.</summary>
    AddressScope = 171,
    /// <summary>Certificate info.</summary>
    CertInfo = 172,
    /// <summary>Username.</summary>
    Username = 10173,
    /// <summary>Password.</summary>
    Password = 10174,
    /// <summary>Proxy username.</summary>
    ProxyUsername = 10175,
    /// <summary>Proxy password.</summary>
    ProxyPassword = 10176,
    /// <summary>No proxy list.</summary>
    NoProxy = 10177,
    /// <summary>TFTP block size.</summary>
    TftpBlkSize = 178,
    /// <summary>SOCKS5 GSSAPI service.</summary>
    Socks5GssapiService = 10179,
    /// <summary>SOCKS5 GSSAPI NEC.</summary>
    Socks5GssapiNec = 180,
    /// <summary>Allowed protocols.</summary>
    Protocols = 181,
    /// <summary>Redirect protocols.</summary>
    RedirProtocols = 182,
    /// <summary>SSH known hosts file.</summary>
    SshKnownHosts = 10183,
    /// <summary>SSH key callback.</summary>
    SshKeyFunction = 20184,
    /// <summary>SSH key data.</summary>
    SshKeyData = 10185,
    /// <summary>MAIL FROM.</summary>
    MailFrom = 10186,
    /// <summary>MAIL RCPT.</summary>
    MailRcpt = 10187,
    /// <summary>FTP use PRET.</summary>
    FtpUsePret = 188,
    /// <summary>RTSP request.</summary>
    RtspRequest = 189,
    /// <summary>RTSP session ID.</summary>
    RtspSessionId = 10190,
    /// <summary>RTSP stream URI.</summary>
    RtspStreamUri = 10191,
    /// <summary>RTSP transport.</summary>
    RtspTransport = 10192,
    /// <summary>RTSP client CSEQ.</summary>
    RtspClientCSeq = 193,
    /// <summary>RTSP server CSEQ.</summary>
    RtspServerCSeq = 194,
    /// <summary>Interleave data.</summary>
    InterleaveData = 10195,
    /// <summary>Interleave callback.</summary>
    InterleaveFunction = 20196,
    /// <summary>Wildcard matching.</summary>
    WildcardMatch = 197,
    /// <summary>Chunk begin callback.</summary>
    ChunkBgnFunction = 20198,
    /// <summary>Chunk end callback.</summary>
    ChunkEndFunction = 20199,
    /// <summary>FNMatch callback.</summary>
    FnMatchFunction = 20200,
    /// <summary>Chunk data.</summary>
    ChunkData = 10201,
    /// <summary>FNMatch data.</summary>
    FnMatchData = 10202,
    /// <summary>Custom DNS resolve.</summary>
    Resolve = 10203,
    /// <summary>TLS auth username.</summary>
    TlsAuthUsername = 10204,
    /// <summary>TLS auth password.</summary>
    TlsAuthPassword = 10205,
    /// <summary>TLS auth type.</summary>
    TlsAuthType = 10206,
    /// <summary>Transfer encoding.</summary>
    TransferEncoding = 207,
    /// <summary>Close socket callback.</summary>
    CloseSocketFunction = 20208,
    /// <summary>Close socket data.</summary>
    CloseSocketData = 10209,
    /// <summary>GSSAPI delegation.</summary>
    GssapiDelegation = 210,
    /// <summary>DNS servers.</summary>
    DnsServers = 10211,
    /// <summary>Accept timeout in ms.</summary>
    AcceptTimeoutMs = 212,
    /// <summary>TCP keepalive.</summary>
    TcpKeepalive = 213,
    /// <summary>TCP keepalive idle time.</summary>
    TcpKeepidle = 214,
    /// <summary>TCP keepalive interval.</summary>
    TcpKeepintvl = 215,
    /// <summary>SSL options.</summary>
    SslOptions = 216,
    /// <summary>MAIL AUTH.</summary>
    MailAuth = 10217,
    /// <summary>SASL IR.</summary>
    SaslIr = 218,
    /// <summary>Transfer info callback.</summary>
    XferInfoFunction = 20219,
    /// <summary>OAuth2 bearer token.</summary>
    XOauth2Bearer = 10220,
    /// <summary>DNS interface.</summary>
    DnsInterface = 10221,
    /// <summary>DNS local IPv4.</summary>
    DnsLocalIp4 = 10222,
    /// <summary>DNS local IPv6.</summary>
    DnsLocalIp6 = 10223,
    /// <summary>Login options.</summary>
    LoginOptions = 10224,
    /// <summary>Enable NPN (deprecated).</summary>
    SslEnableNpn = 225,
    /// <summary>Enable ALPN.</summary>
    SslEnableAlpn = 226,
    /// <summary>Expect 100 timeout in ms.</summary>
    Expect100TimeoutMs = 227,
    /// <summary>Proxy headers.</summary>
    ProxyHeader = 10228,
    /// <summary>Header options.</summary>
    HeaderOpt = 229,
    /// <summary>Pinned public key.</summary>
    PinnedPublicKey = 10230,
    /// <summary>Unix socket path.</summary>
    UnixSocketPath = 10231,
    /// <summary>SSL verify status.</summary>
    SslVerifyStatus = 232,
    /// <summary>SSL false start.</summary>
    SslFalseStart = 233,
    /// <summary>Path as-is.</summary>
    PathAsIs = 234,
    /// <summary>Proxy service name.</summary>
    ProxyServiceName = 10235,
    /// <summary>Service name.</summary>
    ServiceName = 10236,
    /// <summary>Pipe wait.</summary>
    PipeWait = 237,
    /// <summary>Default protocol.</summary>
    DefaultProtocol = 10238,
    /// <summary>Stream weight.</summary>
    StreamWeight = 239,
    /// <summary>Stream depends.</summary>
    StreamDepends = 10240,
    /// <summary>Stream depends exclusively.</summary>
    StreamDependsE = 10241,
    /// <summary>TFTP no options.</summary>
    TftpNoOptions = 242,
    /// <summary>Connect to host:port.</summary>
    ConnectTo = 10243,
    /// <summary>TCP fast open.</summary>
    TcpFastOpen = 244,
    /// <summary>Keep sending on error.</summary>
    KeepSendingOnError = 245,
    /// <summary>Proxy CA info.</summary>
    ProxyCaInfo = 10246,
    /// <summary>Proxy CA path.</summary>
    ProxyCaPath = 10247,
    /// <summary>Proxy SSL verify peer.</summary>
    ProxySslVerifyPeer = 248,
    /// <summary>Proxy SSL verify host.</summary>
    ProxySslVerifyHost = 249,
    /// <summary>Proxy SSL version.</summary>
    ProxySslVersion = 250,
    /// <summary>Proxy TLS auth username.</summary>
    ProxyTlsAuthUsername = 10251,
    /// <summary>Proxy TLS auth password.</summary>
    ProxyTlsAuthPassword = 10252,
    /// <summary>Proxy TLS auth type.</summary>
    ProxyTlsAuthType = 10253,
    /// <summary>Proxy SSL cert.</summary>
    ProxySslCert = 10254,
    /// <summary>Proxy SSL cert type.</summary>
    ProxySslCertType = 10255,
    /// <summary>Proxy SSL key.</summary>
    ProxySslKey = 10256,
    /// <summary>Proxy SSL key type.</summary>
    ProxySslKeyType = 10257,
    /// <summary>Proxy key password.</summary>
    ProxyKeyPasswd = 10258,
    /// <summary>Proxy SSL cipher list.</summary>
    ProxySslCipherList = 10259,
    /// <summary>Proxy CRL file.</summary>
    ProxyCrlFile = 10260,
    /// <summary>Proxy SSL options.</summary>
    ProxySslOptions = 261,
    /// <summary>Pre-proxy.</summary>
    PreProxy = 10262,
    /// <summary>Proxy pinned public key.</summary>
    ProxyPinnedPublicKey = 10263,
    /// <summary>Abstract Unix socket.</summary>
    AbstractUnixSocket = 10264,
    /// <summary>Suppress CONNECT headers.</summary>
    SuppressConnectHeaders = 265,
    /// <summary>Request target.</summary>
    RequestTarget = 10266,
    /// <summary>SOCKS5 auth.</summary>
    Socks5Auth = 267,
    /// <summary>SSH compression.</summary>
    SshCompression = 268,
    /// <summary>MIME POST.</summary>
    MimePost = 10269,
    /// <summary>Large time value.</summary>
    TimeValueLarge = 30270,
    /// <summary>Happy eyeballs timeout in ms.</summary>
    HappyEyeballsTimeoutMs = 271,
    /// <summary>Resolver start callback.</summary>
    ResolverStartFunction = 20272,
    /// <summary>Resolver start data.</summary>
    ResolverStartData = 10273,
    /// <summary>HAProxy protocol.</summary>
    HaProxyProtocol = 274,
    /// <summary>DNS shuffle addresses.</summary>
    DnsShuffleAddresses = 275,
    /// <summary>TLS 1.3 ciphers.</summary>
    Tls13Ciphers = 10276,
    /// <summary>Proxy TLS 1.3 ciphers.</summary>
    ProxyTls13Ciphers = 10277,
    /// <summary>Disallow username in URL.</summary>
    DisallowUsernameInUrl = 278,
    /// <summary>DoH URL.</summary>
    DohUrl = 10279,
    /// <summary>Upload buffer size.</summary>
    UploadBufferSize = 280,
    /// <summary>Upkeep interval in ms.</summary>
    UpkeepIntervalMs = 281,
    /// <summary>CURLU handle.</summary>
    CurlU = 10282,
    /// <summary>Trailer callback.</summary>
    TrailerFunction = 20283,
    /// <summary>Trailer data.</summary>
    TrailerData = 10284,
    /// <summary>Allow HTTP/0.9.</summary>
    Http09Allowed = 285,
    /// <summary>Alt-Svc control.</summary>
    AltSvcCtrl = 286,
    /// <summary>Alt-Svc file.</summary>
    AltSvc = 10287,
    /// <summary>Max age connection.</summary>
    MaxAgeConn = 288,
    /// <summary>SASL authzid.</summary>
    SaslAuthzid = 10289,
    /// <summary>MAIL RCPT allow fails.</summary>
    MailRcptAllowFails = 290,
    /// <summary>SSL cert blob.</summary>
    SslCertBlob = 40291,
    /// <summary>SSL key blob.</summary>
    SslKeyBlob = 40292,
    /// <summary>Proxy SSL cert blob.</summary>
    ProxySslCertBlob = 40293,
    /// <summary>Proxy SSL key blob.</summary>
    ProxySslKeyBlob = 40294,
    /// <summary>Issuer cert blob.</summary>
    IssuerCertBlob = 40295,
    /// <summary>Proxy issuer cert.</summary>
    ProxyIssuerCert = 10296,
    /// <summary>Proxy issuer cert blob.</summary>
    ProxyIssuerCertBlob = 40297,
    /// <summary>SSL EC curves.</summary>
    SslEcCurves = 10298,
    /// <summary>HSTS control.</summary>
    HstsCtrl = 299,
    /// <summary>HSTS file.</summary>
    Hsts = 10300,
    /// <summary>HSTS read callback.</summary>
    HstsReadFunction = 20301,
    /// <summary>HSTS read data.</summary>
    HstsReadData = 10302,
    /// <summary>HSTS write callback.</summary>
    HstsWriteFunction = 20303,
    /// <summary>HSTS write data.</summary>
    HstsWriteData = 10304,
    /// <summary>AWS SigV4.</summary>
    AwsSigv4 = 10305,
    /// <summary>DoH SSL verify peer.</summary>
    DohSslVerifyPeer = 306,
    /// <summary>DoH SSL verify host.</summary>
    DohSslVerifyHost = 307,
    /// <summary>DoH SSL verify status.</summary>
    DohSslVerifyStatus = 308,
    /// <summary>CA info blob.</summary>
    CaInfoBlob = 40309,
    /// <summary>Proxy CA info blob.</summary>
    ProxyCaInfoBlob = 40310,
    /// <summary>SSH host public key SHA256.</summary>
    SshHostPublicKeySha256 = 10311,
    /// <summary>Pre-request callback.</summary>
    PreReqFunction = 20312,
    /// <summary>Pre-request data.</summary>
    PreReqData = 10313,
    /// <summary>Max lifetime connection.</summary>
    MaxLifetimeConn = 314,
    /// <summary>MIME options.</summary>
    MimeOptions = 315,
    /// <summary>SSH host key callback.</summary>
    SshHostKeyFunction = 20316,
    /// <summary>SSH host key data.</summary>
    SshHostKeyData = 10317,
    /// <summary>Protocols string.</summary>
    ProtocolsStr = 10318,
    /// <summary>Redirect protocols string.</summary>
    RedirProtocolsStr = 10319,
    /// <summary>WebSocket options.</summary>
    WsOptions = 320,
    /// <summary>CA cache timeout.</summary>
    CaCacheTimeout = 321,
    /// <summary>Quick exit.</summary>
    QuickExit = 322,
    /// <summary>HAProxy client IP.</summary>
    HaProxyClientIp = 10323,
    /// <summary>Server response timeout in ms.</summary>
    ServerResponseTimeoutMs = 324,
    /// <summary>ECH (Encrypted Client Hello).</summary>
    Ech = 10325,
    /// <summary>TCP keepalive count.</summary>
    TcpKeepcnt = 326,
    /// <summary>Upload flags.</summary>
    UploadFlags = 327,
    /// <summary>SSL signature algorithms.</summary>
    SslSignatureAlgorithms = 10328,

    // curl-impersonate specific options
    /// <summary>Base HTTP headers (curl-impersonate).</summary>
    HttpBaseHeader = 11000,
    /// <summary>SSL signature hash algorithms (curl-impersonate).</summary>
    SslSigHashAlgs = 11001,
    /// <summary>Enable ALPS (curl-impersonate).</summary>
    SslEnableAlps = 1002,
    /// <summary>SSL certificate compression (curl-impersonate).</summary>
    SslCertCompression = 11003,
    /// <summary>Enable SSL ticket (curl-impersonate).</summary>
    SslEnableTicket = 1004,
    /// <summary>HTTP/2 pseudo headers order (curl-impersonate).</summary>
    Http2PseudoHeadersOrder = 11005,
    /// <summary>HTTP/2 settings (curl-impersonate).</summary>
    Http2Settings = 11006,
    /// <summary>SSL permute extensions (curl-impersonate).</summary>
    SslPermuteExtensions = 1007,
    /// <summary>HTTP/2 window update (curl-impersonate).</summary>
    Http2WindowUpdate = 1008,
    /// <summary>HTTP/2 streams (curl-impersonate).</summary>
    Http2Streams = 11010,
    /// <summary>TLS GREASE (curl-impersonate).</summary>
    TlsGrease = 1011,
    /// <summary>TLS extension order (curl-impersonate).</summary>
    TlsExtensionOrder = 11012,
    /// <summary>Stream exclusive (curl-impersonate).</summary>
    StreamExclusive = 1013,
    /// <summary>TLS key usage no check (curl-impersonate).</summary>
    TlsKeyUsageNoCheck = 1014,
    /// <summary>TLS signed cert timestamps (curl-impersonate).</summary>
    TlsSignedCertTimestamps = 1015,
    /// <summary>TLS status request (curl-impersonate).</summary>
    TlsStatusRequest = 1016,
    /// <summary>TLS delegated credentials (curl-impersonate).</summary>
    TlsDelegatedCredentials = 11017,
    /// <summary>TLS record size limit (curl-impersonate).</summary>
    TlsRecordSizeLimit = 1018,
    /// <summary>TLS key shares limit (curl-impersonate).</summary>
    TlsKeySharesLimit = 1019,
    /// <summary>Use new ALPS codepoint (curl-impersonate).</summary>
    TlsUseNewAlpsCodepoint = 1020,
    /// <summary>HTTP/2 no priority (curl-impersonate).</summary>
    Http2NoPriority = 1021,
    /// <summary>Proxy credential no reuse (curl-impersonate).</summary>
    ProxyCredentialNoReuse = 1022,
}
