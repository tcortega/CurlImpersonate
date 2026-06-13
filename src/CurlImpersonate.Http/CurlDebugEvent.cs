using System.Text;

namespace CurlImpersonate.Http;

/// <summary>
/// A single libcurl verbose/debug callback event.
/// </summary>
public sealed class CurlDebugEvent
{
    private readonly byte[] _data;

    internal CurlDebugEvent(CurlDebugInfoType type, byte[] data)
    {
        Type = type;
        _data = data;
    }

    /// <summary>
    /// Event category reported by libcurl.
    /// </summary>
    public CurlDebugInfoType Type { get; }

    /// <summary>
    /// Raw bytes associated with the event.
    /// </summary>
    public ReadOnlyMemory<byte> Data => _data;

    /// <summary>
    /// Decodes the event data as UTF-8 text.
    /// </summary>
    public string GetText() => Encoding.UTF8.GetString(_data);
}
