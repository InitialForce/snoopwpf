namespace SnoopWPF.Agent.Injection;

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#if NET6_0_OR_GREATER
using System.Buffers.Binary;
#endif

/// <summary>
/// Framed JSON protocol helpers: {4-byte LE length}{UTF-8 JSON}.
/// Serialization is dual-mode: DataContractJsonSerializer on net462, System.Text.Json on net6+.
/// </summary>
internal static class JsonFramedSerializer
{
    private const int MaxFrameSize = SnoopWPF.Agent.Contracts.Protocol.ProtocolConstants.MaxFrameSize;

    // -----------------------------------------------------------------
    // Frame I/O
    // -----------------------------------------------------------------

    /// <summary>Writes a framed message: 4-byte LE length header followed by the payload bytes.</summary>
    public static async Task WriteFrameAsync(Stream stream, byte[] payload, CancellationToken ct)
    {
        if (payload.Length > MaxFrameSize)
        {
            throw new InvalidOperationException($"Frame size {payload.Length} exceeds maximum {MaxFrameSize}.");
        }

        var header = new byte[4];
#if NET6_0_OR_GREATER
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
#else
        // Explicit little-endian encoding, portable across any endianness.
        var len = payload.Length;
        header[0] = (byte)(len & 0xFF);
        header[1] = (byte)((len >> 8) & 0xFF);
        header[2] = (byte)((len >> 16) & 0xFF);
        header[3] = (byte)((len >> 24) & 0xFF);
#endif
        await stream.WriteAsync(header, 0, 4, ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, 0, payload.Length, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Reads a framed message. Returns null on clean EOF (pipe closed).</summary>
    public static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        var read = await ReadExactAsync(stream, header, 0, 4, ct).ConfigureAwait(false);
        if (read == 0)
        {
            return null; // clean disconnect
        }

        if (read < 4)
        {
            throw new IOException("Unexpected EOF reading frame header.");
        }

#if NET6_0_OR_GREATER
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
#else
        // Explicit little-endian decoding, portable across any endianness.
        var length = header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24);
#endif
        if (length < 0 || length > MaxFrameSize)
        {
            throw new InvalidOperationException($"Frame length {length} is out of range (max {MaxFrameSize}).");
        }

        var payload = new byte[length];
        var payloadRead = await ReadExactAsync(stream, payload, 0, length, ct).ConfigureAwait(false);
        if (payloadRead < length)
        {
            throw new IOException($"Unexpected EOF reading frame payload (expected {length}, got {payloadRead}).");
        }

        return payload;
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        return totalRead;
    }

    // -----------------------------------------------------------------
    // Serialization (dual-mode)
    // -----------------------------------------------------------------

    /// <summary>Serialize <paramref name="value"/> to UTF-8 JSON bytes.</summary>
    public static byte[] Serialize<T>(T value)
    {
#if NET6_0_OR_GREATER
        var json = System.Text.Json.JsonSerializer.Serialize(
            value,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            });
        return Encoding.UTF8.GetBytes(json);
#else
        var ms = new MemoryStream();
        var dcs = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(T));
        dcs.WriteObject(ms, value);
        return ms.ToArray();
#endif
    }

    /// <summary>Deserialize UTF-8 JSON bytes to <typeparamref name="T"/>.</summary>
    public static T Deserialize<T>(byte[] bytes)
    {
#if NET6_0_OR_GREATER
        // FX6-C3: PropertyNameCaseInsensitive = false to match DCJS net462 case-sensitive behaviour.
        var result = System.Text.Json.JsonSerializer.Deserialize<T>(
            bytes,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = false,
            });
        return result ?? throw new InvalidOperationException("Deserialization returned null.");
#else
        var ms = new MemoryStream(bytes);
        var dcs = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(T));
        return (T)(dcs.ReadObject(ms) ?? throw new InvalidOperationException("Deserialization returned null."));
#endif
    }

    /// <summary>Deserialize UTF-8 JSON string to <typeparamref name="T"/>.</summary>
    public static T DeserializeString<T>(string json)
    {
        return Deserialize<T>(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>Serialize <paramref name="value"/> to a JSON string.</summary>
    public static string SerializeToString<T>(T value)
    {
        return Encoding.UTF8.GetString(Serialize(value));
    }
}
