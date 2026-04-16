namespace SnoopWPF.Agent.Remote;

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SnoopWPF.Agent.Contracts.Protocol;

/// <summary>
/// Reads and writes framed JSON messages over a <see cref="Stream"/>.
/// Frame format: 4-byte little-endian length prefix followed by a UTF-8 JSON body.
/// Max frame size is <see cref="ProtocolConstants.MaxFrameSize"/> bytes.
/// </summary>
internal sealed class FramedJsonTransport
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly Stream stream;
    private readonly byte[] lengthBuffer = new byte[4];

    internal FramedJsonTransport(Stream stream)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <summary>
    /// Serializes <paramref name="value"/> as JSON and sends it as a framed message.
    /// </summary>
    internal async Task SendAsync<T>(T value, CancellationToken ct)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);

        if (body.Length > ProtocolConstants.MaxFrameSize)
        {
            throw new InvalidOperationException(
                $"Outgoing frame size {body.Length} exceeds MaxFrameSize {ProtocolConstants.MaxFrameSize}.");
        }

        BinaryPrimitives.WriteInt32LittleEndian(this.lengthBuffer, body.Length);

        await this.stream.WriteAsync(this.lengthBuffer, 0, 4, ct).ConfigureAwait(false);
        await this.stream.WriteAsync(body, 0, body.Length, ct).ConfigureAwait(false);
        await this.stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a framed message and deserializes it as <typeparamref name="T"/>.
    /// Returns <see langword="null"/> if the peer closed the connection cleanly.
    /// Throws <see cref="InvalidOperationException"/> on protocol violations.
    /// </summary>
    internal async Task<T?> ReceiveAsync<T>(CancellationToken ct)
    {
        // Read 4-byte length prefix.
        int bytesRead = 0;
        while (bytesRead < 4)
        {
            int n = await this.stream.ReadAsync(this.lengthBuffer, bytesRead, 4 - bytesRead, ct).ConfigureAwait(false);
            if (n == 0)
            {
                if (bytesRead == 0)
                {
                    // Clean EOF — peer closed.
                    return default;
                }

                throw new EndOfStreamException("Unexpected end of stream while reading frame length prefix.");
            }

            bytesRead += n;
        }

        int frameLength = BinaryPrimitives.ReadInt32LittleEndian(this.lengthBuffer);

        if (frameLength < 0 || frameLength > ProtocolConstants.MaxFrameSize)
        {
            throw new InvalidOperationException(
                $"Frame length {frameLength} is invalid (max={ProtocolConstants.MaxFrameSize}).");
        }

        byte[] bodyBuffer = new byte[frameLength];
        int offset = 0;
        while (offset < frameLength)
        {
            int n = await this.stream.ReadAsync(bodyBuffer, offset, frameLength - offset, ct).ConfigureAwait(false);
            if (n == 0)
            {
                throw new EndOfStreamException(
                    $"Unexpected end of stream reading frame body (got {offset}/{frameLength} bytes).");
            }

            offset += n;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(bodyBuffer, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Failed to deserialize frame as {typeof(T).Name}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Deserializes a JSON string (already received from the wire) into <typeparamref name="T"/>.
    /// Used to decode <c>ResultJson</c> / <c>ParamsJson</c> fields.
    /// </summary>
    internal static T? DeserializeJson<T>(string? json)
    {
        if (json is null)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    /// <summary>
    /// Serializes <paramref name="value"/> to a compact JSON string.
    /// Used to produce <c>ParamsJson</c> fields in <see cref="PipeRequest"/>.
    /// </summary>
    internal static string SerializeJson<T>(T value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }
}
