namespace CurlImpersonate.Http;

/// <summary>
/// Libcurl verbose/debug event categories.
/// </summary>
public enum CurlDebugInfoType
{
    /// <summary>Informational text from libcurl.</summary>
    Text = 0,

    /// <summary>Incoming protocol header bytes.</summary>
    HeaderIn = 1,

    /// <summary>Outgoing protocol header bytes.</summary>
    HeaderOut = 2,

    /// <summary>Incoming body bytes.</summary>
    DataIn = 3,

    /// <summary>Outgoing body bytes.</summary>
    DataOut = 4,

    /// <summary>Incoming TLS bytes.</summary>
    SslDataIn = 5,

    /// <summary>Outgoing TLS bytes.</summary>
    SslDataOut = 6
}
