using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;

namespace CloudlogHelper.Utils;

public static class ProtobufExtension
{
    public static async Task<T?> ParseDelimitedFromAsync<T>(
        this MessageParser<T> parser,
        Stream stream,
        CancellationToken cancellationToken = default)
        where T : IMessage<T>, new()
    {
        if (parser == null) throw new ArgumentNullException(nameof(parser));
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        
        var length = await ReadVarint32Async(stream, cancellationToken).ConfigureAwait(false);
        if (length < 0) return default;

        if (length == 0)
        {
            return parser.ParseFrom(ReadOnlySpan<byte>.Empty);
        }

        var buffer = new byte[length];
        var totalRead = 0;
        while (totalRead < length)
        {
            var bytesRead = await stream.ReadAsync(buffer, totalRead, length - totalRead, cancellationToken)
                                       .ConfigureAwait(false);
            if (bytesRead == 0)
                throw new EndOfStreamException("Unexpected end of stream while reading delimited message body.");
            totalRead += bytesRead;
        }

        return parser.ParseFrom(buffer);
    }

    public static async Task WriteDelimitedToAsync<T>(
        this T message,
        Stream stream,
        CancellationToken cancellationToken = default)
        where T : IMessage<T>
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        if (stream == null) throw new ArgumentNullException(nameof(stream));

        var payload = message.ToByteArray();
        var lengthPrefix = EncodeVarint32((uint)payload.Length);

        await stream.WriteAsync(lengthPrefix, 0, lengthPrefix.Length, cancellationToken)
                    .ConfigureAwait(false);
        await stream.WriteAsync(payload, 0, payload.Length, cancellationToken)
                    .ConfigureAwait(false);
    }

    private static async Task<int> ReadVarint32Async(Stream stream, CancellationToken cancellationToken)
    {
        var result = 0;
        var shift = 0;
        var buffer = new byte[1];

        while (shift < 32)
        {
            var read = await stream.ReadAsync(buffer, 0, 1, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return -1; // 流结束

            var b = buffer[0];
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return result;
            shift += 7;
        }
        
        throw new InvalidDataException("Invalid varint32 encountered.");
    }

    
    private static byte[] EncodeVarint32(uint value)
    {
        var buffer = new byte[10];
        var pos = 0;
        while (value >= 0x80)
        {
            buffer[pos++] = (byte)(value | 0x80);
            value >>= 7;
        }
        buffer[pos++] = (byte)value;
        Array.Resize(ref buffer, pos);
        return buffer;
    }
}