namespace CurlImpersonate.Http;

/// <summary>
/// Advanced curl-impersonate fingerprint overrides.
/// </summary>
public sealed class CurlFingerprintOptions
{
    /// <summary>JA3 TLS fingerprint string: "version,ciphers,extensions,curves,curve_formats".</summary>
    public string? Ja3 { get; set; }

    /// <summary>Whether JA3 parsing should skip setting a fixed TLS extension order.</summary>
    public bool Ja3PermuteExtensions { get; set; }

    /// <summary>Akamai HTTP/2 fingerprint string: "settings|window_update|streams|pseudo_header_order".</summary>
    public string? Akamai { get; set; }

    /// <summary>HTTP/3 PERK-style fingerprint string: "settings|pseudo_header_order|quic_transport_parameters".</summary>
    public string? Http3Fingerprint { get; set; }

    /// <summary>TLS signature hash algorithms.</summary>
    public string? SslSignatureAlgorithms { get; set; }

    /// <summary>TLS certificate compression algorithms.</summary>
    public string? SslCertCompression { get; set; }

    /// <summary>Whether to enable TLS ALPS.</summary>
    public bool? SslEnableAlps { get; set; }

    /// <summary>Whether to enable TLS session tickets.</summary>
    public bool? SslEnableTicket { get; set; }

    /// <summary>HTTP/2 pseudo-header order, such as "masp".</summary>
    public string? Http2PseudoHeaderOrder { get; set; }

    /// <summary>HTTP/2 settings frame values, such as "1:65536;4:6291456".</summary>
    public string? Http2Settings { get; set; }

    /// <summary>Whether to permute TLS extension order.</summary>
    public bool? SslPermuteExtensions { get; set; }

    /// <summary>Initial HTTP/2 window update value.</summary>
    public long? Http2WindowUpdate { get; set; }

    /// <summary>Initial HTTP/2 streams settings.</summary>
    public string? Http2Streams { get; set; }

    /// <summary>Whether to enable TLS GREASE.</summary>
    public bool? TlsGrease { get; set; }

    /// <summary>TLS extension order, such as "0-5-10".</summary>
    public string? TlsExtensionOrder { get; set; }

    /// <summary>HTTP/2 stream exclusiveness.</summary>
    public bool? StreamExclusive { get; set; }

    /// <summary>HTTP/2 stream weight from 1 to 256.</summary>
    public long? Http2StreamWeight { get; set; }

    /// <summary>Whether to disable TLS key-usage checks.</summary>
    public bool? TlsKeyUsageNoCheck { get; set; }

    /// <summary>Whether to enable TLS signed certificate timestamps.</summary>
    public bool? TlsSignedCertificateTimestamps { get; set; }

    /// <summary>Whether to enable TLS status request.</summary>
    public bool? TlsStatusRequest { get; set; }

    /// <summary>Firefox delegated credentials value.</summary>
    public string? TlsDelegatedCredentials { get; set; }

    /// <summary>TLS record size limit.</summary>
    public long? TlsRecordSizeLimit { get; set; }

    /// <summary>TLS key shares limit.</summary>
    public long? TlsKeySharesLimit { get; set; }

    /// <summary>Whether to use the new ALPS codepoint.</summary>
    public bool? TlsUseNewAlpsCodepoint { get; set; }

    /// <summary>Whether to suppress HTTP/2 priority.</summary>
    public bool? Http2NoPriority { get; set; }

    /// <summary>Whether to split cookies into separate Cookie headers.</summary>
    public bool? SplitCookies { get; set; }

    /// <summary>Multipart form boundary style, such as "webkit" or "firefox".</summary>
    public string? FormBoundary { get; set; }

    /// <summary>HTTP/3 pseudo-header order, such as "masp".</summary>
    public string? Http3PseudoHeaderOrder { get; set; }

    /// <summary>HTTP/3 settings frame values.</summary>
    public string? Http3Settings { get; set; }

    /// <summary>QUIC transport parameters.</summary>
    public string? QuicTransportParameters { get; set; }

    /// <summary>HTTP/3 QUIC TLS signature hash algorithms.</summary>
    public string? Http3SignatureHashAlgorithms { get; set; }

    /// <summary>HTTP/3 QUIC TLS extension order.</summary>
    public string? Http3TlsExtensionOrder { get; set; }

    internal void CopyFrom(CurlFingerprintOptions source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Ja3 = source.Ja3;
        Ja3PermuteExtensions = source.Ja3PermuteExtensions;
        Akamai = source.Akamai;
        Http3Fingerprint = source.Http3Fingerprint;
        SslSignatureAlgorithms = source.SslSignatureAlgorithms;
        SslCertCompression = source.SslCertCompression;
        SslEnableAlps = source.SslEnableAlps;
        SslEnableTicket = source.SslEnableTicket;
        Http2PseudoHeaderOrder = source.Http2PseudoHeaderOrder;
        Http2Settings = source.Http2Settings;
        SslPermuteExtensions = source.SslPermuteExtensions;
        Http2WindowUpdate = source.Http2WindowUpdate;
        Http2Streams = source.Http2Streams;
        TlsGrease = source.TlsGrease;
        TlsExtensionOrder = source.TlsExtensionOrder;
        StreamExclusive = source.StreamExclusive;
        Http2StreamWeight = source.Http2StreamWeight;
        TlsKeyUsageNoCheck = source.TlsKeyUsageNoCheck;
        TlsSignedCertificateTimestamps = source.TlsSignedCertificateTimestamps;
        TlsStatusRequest = source.TlsStatusRequest;
        TlsDelegatedCredentials = source.TlsDelegatedCredentials;
        TlsRecordSizeLimit = source.TlsRecordSizeLimit;
        TlsKeySharesLimit = source.TlsKeySharesLimit;
        TlsUseNewAlpsCodepoint = source.TlsUseNewAlpsCodepoint;
        Http2NoPriority = source.Http2NoPriority;
        SplitCookies = source.SplitCookies;
        FormBoundary = source.FormBoundary;
        Http3PseudoHeaderOrder = source.Http3PseudoHeaderOrder;
        Http3Settings = source.Http3Settings;
        QuicTransportParameters = source.QuicTransportParameters;
        Http3SignatureHashAlgorithms = source.Http3SignatureHashAlgorithms;
        Http3TlsExtensionOrder = source.Http3TlsExtensionOrder;
    }
}
