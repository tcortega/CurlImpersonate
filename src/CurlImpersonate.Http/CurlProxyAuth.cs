namespace CurlImpersonate.Http;

/// <summary>
/// Proxy authentication schemes accepted by libcurl.
/// </summary>
[Flags]
public enum CurlProxyAuth : long
{
    /// <summary>No proxy authentication scheme is selected.</summary>
    None = 0,

    /// <summary>HTTP Basic proxy authentication.</summary>
    Basic = 1,

    /// <summary>HTTP Digest proxy authentication.</summary>
    Digest = 2,

    /// <summary>SPNEGO/Kerberos proxy authentication.</summary>
    Negotiate = 4,

    /// <summary>NTLM proxy authentication.</summary>
    Ntlm = 8,

    /// <summary>HTTP Digest IE proxy authentication.</summary>
    DigestIe = 16,

    /// <summary>Bearer token proxy authentication where supported by libcurl.</summary>
    Bearer = 64,

    /// <summary>Allow libcurl to pick any proxy authentication scheme advertised by the proxy.</summary>
    Any = -17,

    /// <summary>Allow libcurl to pick any advertised proxy authentication scheme except Basic.</summary>
    AnySafe = -18
}
