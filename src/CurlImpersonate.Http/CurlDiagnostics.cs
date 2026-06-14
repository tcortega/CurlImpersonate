namespace CurlImpersonate.Http;

/// <summary>
/// Process-wide sink for diagnostic messages emitted by the curl event loop
/// background thread (for example, unexpected loop errors or wake-up failures).
/// These messages originate off the request path and are not tied to a single
/// handler, so the sink is process-global.
/// </summary>
/// <remarks>
/// When no handler is set the messages are written to <see cref="Console.Error"/>,
/// preserving the historical default. Set <see cref="Handler"/> to route them to
/// your own logging system, or set it to a no-op to suppress them. The handler
/// is invoked on the event-loop thread; keep it fast and non-throwing.
/// </remarks>
public static class CurlDiagnostics
{
    /// <summary>
    /// Optional handler invoked for each diagnostic message. When null, messages
    /// are written to <see cref="Console.Error"/>.
    /// </summary>
    public static Action<string>? Handler { get; set; }

    internal static void Report(string message)
    {
        var handler = Handler;
        if (handler is null)
        {
            Console.Error.WriteLine(message);
            return;
        }

        try
        {
            handler(message);
        }
        catch
        {
            // A diagnostic sink must never destabilize the event loop thread.
        }
    }
}
