namespace CurlImpersonate.Enums;

/// <summary>
/// CURLMcode return values from curl_multi_* functions.
/// </summary>
public enum CurlMultiCode
{
    /// <summary>All fine.</summary>
    Ok = 0,
    /// <summary>Invalid multi handle.</summary>
    BadHandle = 1,
    /// <summary>Invalid easy handle.</summary>
    BadEasyHandle = 2,
    /// <summary>Out of memory.</summary>
    OutOfMemory = 3,
    /// <summary>Internal error.</summary>
    InternalError = 4,
    /// <summary>Bad socket argument.</summary>
    BadSocket = 5,
    /// <summary>Unknown option.</summary>
    UnknownOption = 6,
    /// <summary>Added already.</summary>
    AddedAlready = 7,
    /// <summary>Recursive API call.</summary>
    RecursiveApiCall = 8,
    /// <summary>Wakeup failure.</summary>
    WakeupFailure = 9,
    /// <summary>Bad function argument.</summary>
    BadFunctionArgument = 10,
    /// <summary>Aborted by callback.</summary>
    AbortedByCallback = 11,
    /// <summary>Unrecoverable poll.</summary>
    UnrecoverablePoll = 12,
}
