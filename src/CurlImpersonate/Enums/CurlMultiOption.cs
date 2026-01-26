namespace CurlImpersonate.Enums;

/// <summary>
/// CURLMOPT_* options for curl_multi_setopt.
/// </summary>
public enum CurlMultiOption
{
    /// <summary>Socket callback function.</summary>
    SocketFunction = 20001,
    /// <summary>Socket callback data.</summary>
    SocketData = 10002,
    /// <summary>Enable pipelining/multiplexing.</summary>
    Pipelining = 3,
    /// <summary>Timer callback function.</summary>
    TimerFunction = 20004,
    /// <summary>Timer callback data.</summary>
    TimerData = 10005,
    /// <summary>Maximum number of connections to keep in pool.</summary>
    MaxConnects = 6,
    /// <summary>Maximum number of connections to a single host.</summary>
    MaxHostConnections = 7,
    /// <summary>Maximum pipeline length.</summary>
    MaxPipelineLength = 8,
    /// <summary>Content length penalty size.</summary>
    ContentLengthPenaltySize = 30009,
    /// <summary>Chunk length penalty size.</summary>
    ChunkLengthPenaltySize = 30010,
    /// <summary>Pipelining site blacklist.</summary>
    PipeliningSiteBl = 10011,
    /// <summary>Pipelining server blacklist.</summary>
    PipeliningServerBl = 10012,
    /// <summary>Maximum total connections.</summary>
    MaxTotalConnections = 13,
    /// <summary>Push callback function.</summary>
    PushFunction = 20014,
    /// <summary>Push callback data.</summary>
    PushData = 10015,
    /// <summary>Maximum concurrent streams.</summary>
    MaxConcurrentStreams = 16,
}
