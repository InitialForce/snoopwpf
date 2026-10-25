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
/// Frames are delivered in the order they are written (in-order guarantee).
/// </summary>
public sealed class FramedJsonTransport : IDisposable
{
    // FX6-C3: use AgentJsonOptions.Framed (case-sensitive) to match DCJS net462 behaviour.
    private static readonly JsonSerializerOptions JsonOptions = AgentJsonOptions.Framed;

    private readonly Stream stream;

    // Serialises concurrent writes so multiple callers cannot interleave frame bytes.
    private readonly SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);

    public FramedJsonTransport(Stream stream)
    {
        this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this.writeLock.Dispose();
    }

    /// <summary>
    /// Serializes <paramref name="value"/> as JSON and sends it as a framed message.
    /// Concurrent callers are serialized via an internal write lock to guarantee in-order delivery.
    /// </summary>
    /// <remarks>
    /// <paramref name="ct"/> bounds the wait for the write lock and for the write to finish, but it
    /// never cancels the write itself. A pipe write that does not fit the pipe's buffer stays pending
    /// until the reader consumes the bytes (under Wine that is every write to a pipe with the zero-size
    /// buffer .NET asks for), and cancelling a pending write drops whatever the reader has not consumed yet. The reader would then take the next
    /// frame's bytes as the rest of this one, and the connection would be lost. So once the frame has
    /// started, it is always written in full, and the write lock is held until it is, so a later frame
    /// cannot overtake it.
    /// </remarks>
    public async Task SendAsync<T>(T value, CancellationToken ct)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);

        if (body.Length > ProtocolConstants.MaxFrameSize)
        {
            throw new InvalidOperationException(
                $"Outgoing frame size {body.Length} exceeds MaxFrameSize {ProtocolConstants.MaxFrameSize}.");
        }

        // One buffer for the length prefix and the body, so the frame goes out in a single write.
        // Method-local, so SendAsync and ReceiveAsync never share state.
        byte[] frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, body.Length);
        Buffer.BlockCopy(body, 0, frame, 4, body.Length);

        await this.writeLock.WaitAsync(ct).ConfigureAwait(false);

        Task write;
        try
        {
            write = this.WriteFrameAsync(frame);
        }
        catch
        {
            this.writeLock.Release();
            throw;
        }

        _ = write.ContinueWith(
            static (t, state) =>
            {
                // Observe a failure nobody is waiting for any more; the next read or write reports it.
                _ = t.Exception;
                try
                {
                    ((SemaphoreSlim)state!).Release();
                }
                catch (ObjectDisposedException)
                {
                    // The transport was disposed while the write was in flight.
                }
            },
            this.writeLock,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        await write.WaitAsync(ct).ConfigureAwait(false);
    }

    private async Task WriteFrameAsync(byte[] frame)
    {
        await this.stream.WriteAsync(frame, 0, frame.Length, CancellationToken.None).ConfigureAwait(false);
        await this.stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a framed message and deserializes it as <typeparamref name="T"/>.
    /// Returns <see langword="null"/> if the peer closed the connection cleanly (EOF at frame boundary).
    /// Throws <see cref="EndOfStreamException"/> on a partial disconnect mid-frame.
    /// Throws <see cref="InvalidOperationException"/> on protocol violations.
    /// </summary>
    public async Task<T?> ReceiveAsync<T>(CancellationToken ct)
    {
        // Read 4-byte length prefix.
        // Use a method-local buffer so SendAsync and ReceiveAsync never share state.
        byte[] receiveLengthBuffer = new byte[4];
        int bytesRead = 0;
        while (bytesRead < 4)
        {
            int n = await this.stream.ReadAsync(receiveLengthBuffer, bytesRead, 4 - bytesRead, ct).ConfigureAwait(false);
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

        int frameLength = BinaryPrimitives.ReadInt32LittleEndian(receiveLengthBuffer);

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
    public static T? DeserializeJson<T>(string? json)
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
    public static string SerializeJson<T>(T value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }
}
