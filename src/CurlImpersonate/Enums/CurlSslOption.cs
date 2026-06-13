namespace CurlImpersonate.Enums;

/// <summary>
/// CURLSSLOPT_* flags for CURLOPT_SSL_OPTIONS and CURLOPT_PROXY_SSL_OPTIONS.
/// </summary>
[Flags]
public enum CurlSslOption : long
{
    /// <summary>No SSL option flags.</summary>
    None = 0,
    /// <summary>Allow the BEAST TLS1.0 workaround tradeoff.</summary>
    AllowBeast = 1 << 0,
    /// <summary>Disable certificate revocation checks.</summary>
    NoRevoke = 1 << 1,
    /// <summary>Do not accept partial certificate chains.</summary>
    NoPartialChain = 1 << 2,
    /// <summary>Ignore revocation offline or missing-distribution-point errors.</summary>
    RevokeBestEffort = 1 << 3,
    /// <summary>Use the operating system CA store in addition to the configured CA bundle.</summary>
    NativeCa = 1 << 4,
    /// <summary>Automatically locate and use a client certificate for authentication.</summary>
    AutoClientCert = 1 << 5,
}
