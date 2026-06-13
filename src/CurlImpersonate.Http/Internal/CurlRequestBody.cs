namespace CurlImpersonate.Http.Internal;

internal sealed class CurlRequestBody
{
    private CurlRequestBody(byte[]? bytes, Stream? stream, long? length)
    {
        Bytes = bytes;
        Stream = stream;
        Length = length;
    }

    public byte[]? Bytes { get; }

    public Stream? Stream { get; }

    public long? Length { get; }

    public bool IsStreaming => Stream is not null;

    public static CurlRequestBody Buffered(byte[] bytes) => new(bytes, null, bytes.LongLength);

    public static CurlRequestBody Streaming(Stream stream, long? length)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new InvalidOperationException("Request content stream must be readable.");

        if (length is < 0)
            throw new InvalidOperationException("Request content length cannot be negative.");

        return new(null, stream, length);
    }
}
