namespace SnoopWPF.Agent.InjectionTests;

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts.Protocol;
using SnoopWPF.Agent.Remote;

/// <summary>
/// Validates wire-format compatibility between Remote's <see cref="FramedJsonTransport"/>
/// and the raw framing format used by the Injection-side serialiser.
///
/// Both sides use {4-byte LE length}{UTF-8 JSON}. On net8.0-windows both use System.Text.Json,
/// so this test confirms the shared format contract is stable.
/// </summary>
[TestFixture]
public sealed class FramingTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Writes a raw framed message (as the injection side would) into a byte[].</summary>
    private static byte[] BuildRawFrame(string json)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        var ms = new MemoryStream(4 + body.Length);
        ms.Write(BitConverter.GetBytes(body.Length), 0, 4);
        ms.Write(body, 0, body.Length);
        return ms.ToArray();
    }

    /// <summary>Parses the length header written by <see cref="FramedJsonTransport"/> from a MemoryStream.</summary>
    private static (int Length, string Json) ReadRawFrame(byte[] data)
    {
        int length = BitConverter.ToInt32(data, 0);
        string json = Encoding.UTF8.GetString(data, 4, length);
        return (length, json);
    }

    // -----------------------------------------------------------------------
    // Remote writes → Raw reader succeeds
    // -----------------------------------------------------------------------

    [Test]
    public async Task FramedJsonTransport_Write_ProducesLengthPrefixedUtf8Json()
    {
        using var ms = new MemoryStream();
        var transport = new FramedJsonTransport(ms);

        var request = new PipeRequest { Id = 1, Method = "GetWindows", ParamsJson = "{\"includeHidden\":false}" };
        await transport.SendAsync(request, CancellationToken.None);

        byte[] written = ms.ToArray();

        // Must have at least 5 bytes: 4-byte header + at least 1 JSON byte
        Assert.That(written.Length, Is.GreaterThan(4));

        var (length, json) = ReadRawFrame(written);

        Assert.That(length, Is.EqualTo(written.Length - 4));
        Assert.That(json, Does.Contain("\"id\""));
        Assert.That(json, Does.Contain("\"method\""));
        Assert.That(json, Does.Contain("GetWindows"));
    }

    [Test]
    public async Task FramedJsonTransport_Write_LengthHeader_IsLittleEndian()
    {
        using var ms = new MemoryStream();
        var transport = new FramedJsonTransport(ms);

        var payload = new PipeRequest { Id = 99, Method = "GetSessionInfo", ParamsJson = "{}" };
        await transport.SendAsync(payload, CancellationToken.None);

        byte[] written = ms.ToArray();

        // Little-endian check: parse manually and compare to BitConverter.ToInt32
        int lengthLE = written[0] | (written[1] << 8) | (written[2] << 16) | (written[3] << 24);
        int lengthBE = BitConverter.ToInt32(new byte[] { written[3], written[2], written[1], written[0] }, 0);
        int bodyLength = written.Length - 4;

        Assert.That(lengthLE, Is.EqualTo(bodyLength), "Length header must be little-endian");
        Assert.That(lengthBE, Is.Not.EqualTo(bodyLength), "Big-endian interpretation must NOT match unless body length happens to be symmetric");
    }

    // -----------------------------------------------------------------------
    // Raw writer → Remote reads successfully
    // -----------------------------------------------------------------------

    [Test]
    public async Task RawInjectionSideFrame_FramedJsonTransportCanRead_PipeRequest()
    {
        // Simulate the injection side writing a PipeRequest frame manually,
        // matching the {4-byte LE length}{UTF-8 JSON} protocol.
        string json = "{\"id\":42,\"method\":\"GetProperties\",\"paramsJson\":\"{\\\"nodeId\\\":\\\"0:1\\\"}\"}";
        byte[] rawFrame = BuildRawFrame(json);

        using var ms = new MemoryStream(rawFrame);
        var transport = new FramedJsonTransport(ms);

        var result = await transport.ReceiveAsync<PipeRequest>(CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(42));
        Assert.That(result.Method, Is.EqualTo("GetProperties"));
        Assert.That(result.ParamsJson, Does.Contain("nodeId"));
    }

    [Test]
    public async Task RawInjectionSideFrame_FramedJsonTransportCanRead_HandshakeChallenge()
    {
        string json = "{\"sessionToken\":\"tok-xyz\",\"protocolVersion\":1}";
        byte[] rawFrame = BuildRawFrame(json);

        using var ms = new MemoryStream(rawFrame);
        var transport = new FramedJsonTransport(ms);

        var result = await transport.ReceiveAsync<HandshakeChallenge>(CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.SessionToken, Is.EqualTo("tok-xyz"));
        Assert.That(result.ProtocolVersion, Is.EqualTo(1));
    }

    [Test]
    public async Task RawInjectionSideFrame_FramedJsonTransportCanRead_HandshakeResponse()
    {
        string json =
            "{\"protocolVersion\":1,\"agentVersion\":\"0.1.0\",\"targetRuntime\":\".NET 8.0\"," +
            "\"sessionToken\":\"tok-xyz\",\"dispatchers\":[],\"capabilities\":[\"inspection\"]}";
        byte[] rawFrame = BuildRawFrame(json);

        using var ms = new MemoryStream(rawFrame);
        var transport = new FramedJsonTransport(ms);

        var result = await transport.ReceiveAsync<HandshakeResponse>(CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ProtocolVersion, Is.EqualTo(1));
        Assert.That(result.SessionToken, Is.EqualTo("tok-xyz"));
        Assert.That(result.Capabilities, Does.Contain("inspection"));
    }

    [Test]
    public async Task RawInjectionSideFrame_FramedJsonTransportCanRead_PipeResponse_WithResult()
    {
        string json = "{\"id\":7,\"resultJson\":\"{\\\"processName\\\":\\\"TestApp\\\",\\\"pid\\\":1234}\"}";
        byte[] rawFrame = BuildRawFrame(json);

        using var ms = new MemoryStream(rawFrame);
        var transport = new FramedJsonTransport(ms);

        var result = await transport.ReceiveAsync<PipeResponse>(CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(7));
        Assert.That(result.ResultJson, Does.Contain("processName"));
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task RawInjectionSideFrame_FramedJsonTransportCanRead_PipeResponse_WithError()
    {
        string json =
            "{\"id\":99,\"error\":{\"code\":\"NodeNotFound\",\"message\":\"Node not found\",\"suggestion\":\"\"}}";
        byte[] rawFrame = BuildRawFrame(json);

        using var ms = new MemoryStream(rawFrame);
        var transport = new FramedJsonTransport(ms);

        var result = await transport.ReceiveAsync<PipeResponse>(CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(99));
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error!.Code, Is.EqualTo("NodeNotFound"));
        Assert.That(result.ResultJson, Is.Null);
    }

    [Test]
    public async Task RawInjectionSideFrame_FramedJsonTransportCanRead_CancelPayload()
    {
        string json = "{\"id\":5,\"cancel\":true}";
        byte[] rawFrame = BuildRawFrame(json);

        using var ms = new MemoryStream(rawFrame);
        var transport = new FramedJsonTransport(ms);

        var result = await transport.ReceiveAsync<PipeCancelPayload>(CancellationToken.None);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(5));
        Assert.That(result.Cancel, Is.True);
    }

    // -----------------------------------------------------------------------
    // Bidirectional round-trip (Remote write → raw read → raw write → Remote read)
    // -----------------------------------------------------------------------

    [Test]
    public async Task FullRoundTrip_RemoteWritesRequest_RawReadsAndReplies_RemoteReadsResponse()
    {
        // Simulates: host (Remote) sends a PipeRequest → injection side echoes back a PipeResponse.
        // This is the core cross-layer interop test.

        // Step 1: Remote side serialises a request into bytes.
        var outboundMs = new MemoryStream();
        var hostTransport = new FramedJsonTransport(outboundMs);
        var originalRequest = new PipeRequest { Id = 10, Method = "GetSessionInfo", ParamsJson = "{}" };
        await hostTransport.SendAsync(originalRequest, CancellationToken.None);
        byte[] requestBytes = outboundMs.ToArray();

        // Step 2: "Injection side" reads raw bytes, parses length + body.
        var (requestLength, requestJson) = ReadRawFrame(requestBytes);
        Assert.That(requestLength, Is.GreaterThan(0));
        Assert.That(requestJson, Does.Contain("GetSessionInfo"));

        // Step 3: "Injection side" writes a raw PipeResponse frame back.
        string responseJson =
            "{\"id\":10,\"resultJson\":\"{\\\"processName\\\":\\\"SampleApp\\\",\\\"pid\\\":9999," +
            "\\\"dotnetVersion\\\":\\\".NET 8.0\\\",\\\"mutationEnabled\\\":true," +
            "\\\"dispatchers\\\":[],\\\"capabilities\\\":[]}\"}";
        byte[] responseFrame = BuildRawFrame(responseJson);

        // Step 4: Remote side reads the raw frame via FramedJsonTransport.
        var inboundMs = new MemoryStream(responseFrame);
        var receiverTransport = new FramedJsonTransport(inboundMs);
        var response = await receiverTransport.ReceiveAsync<PipeResponse>(CancellationToken.None);

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Id, Is.EqualTo(10));
        Assert.That(response.ResultJson, Does.Contain("SampleApp"));
        Assert.That(response.Error, Is.Null);
    }

    // -----------------------------------------------------------------------
    // CamelCase property naming round-trip
    // -----------------------------------------------------------------------

    [Test]
    public async Task CamelCaseNaming_FramedJsonTransport_ProducesCamelCaseProperties()
    {
        using var ms = new MemoryStream();
        var transport = new FramedJsonTransport(ms);

        var challenge = new HandshakeChallenge
        {
            SessionToken = "abc",
            ProtocolVersion = 1,
        };
        await transport.SendAsync(challenge, CancellationToken.None);

        byte[] written = ms.ToArray();
        var (_, json) = ReadRawFrame(written);

        // Properties must be camelCase on the wire.
        Assert.That(json, Does.Contain("\"sessionToken\""), "SessionToken must be camelCase");
        Assert.That(json, Does.Contain("\"protocolVersion\""), "ProtocolVersion must be camelCase");
        Assert.That(json, Does.Not.Contain("\"SessionToken\""), "PascalCase must not appear");
    }

    [Test]
    public async Task CamelCaseNaming_RawFrame_CanBeReadByCamelCaseAwareTransport()
    {
        // camelCase property names (as injection DCJS writes on net462, STJ on net6+)
        string json = "{\"sessionToken\":\"mytoken\",\"protocolVersion\":1}";
        byte[] rawFrame = BuildRawFrame(json);

        using var ms = new MemoryStream(rawFrame);
        var transport = new FramedJsonTransport(ms);
        var result = await transport.ReceiveAsync<HandshakeChallenge>(CancellationToken.None);

        Assert.That(result!.SessionToken, Is.EqualTo("mytoken"));
        Assert.That(result.ProtocolVersion, Is.EqualTo(1));
    }

    // -----------------------------------------------------------------------
    // Protocol bounds enforcement
    // -----------------------------------------------------------------------

    [Test]
    public void RawFrame_NegativeLengthHeader_FailsRead()
    {
        byte[] raw = new byte[8];
        BitConverter.GetBytes(-1).CopyTo(raw, 0);

        using var ms = new MemoryStream(raw);
        var transport = new FramedJsonTransport(ms);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await transport.ReceiveAsync<PipeRequest>(CancellationToken.None);
        });
    }

    [Test]
    public void RawFrame_OversizedLengthHeader_FailsRead()
    {
        byte[] raw = new byte[8];
        BitConverter.GetBytes(ProtocolConstants.MaxFrameSize + 1).CopyTo(raw, 0);

        using var ms = new MemoryStream(raw);
        var transport = new FramedJsonTransport(ms);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await transport.ReceiveAsync<PipeRequest>(CancellationToken.None);
        });
    }

    [Test]
    public void RawFrame_MalformedJson_FailsDeserialization()
    {
        byte[] rawFrame = BuildRawFrame("NOT_VALID_JSON{{{{");

        using var ms = new MemoryStream(rawFrame);
        var transport = new FramedJsonTransport(ms);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await transport.ReceiveAsync<PipeRequest>(CancellationToken.None);
        });
    }

    [Test]
    public async Task RawFrame_EmptyStream_ReturnsNull()
    {
        using var ms = new MemoryStream(Array.Empty<byte>());
        var transport = new FramedJsonTransport(ms);

        var result = await transport.ReceiveAsync<PipeRequest>(CancellationToken.None);

        Assert.That(result, Is.Null, "Empty stream must return null (clean EOF)");
    }

    // -----------------------------------------------------------------------
    // Truncated frame (disconnect mid-body)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Writes a valid 4-byte length header that claims N body bytes, but only M &lt; N bytes
    /// follow before the stream ends. Simulates a peer disconnecting mid-frame.
    /// Expected: <see cref="EndOfStreamException"/> — no hang, no garbage return value.
    /// </summary>
    [Test]
    public void TruncatedFrame_BodyShorterThanDeclaredLength_ThrowsEndOfStream()
    {
        const int declaredBodyLength = 64;
        const int actualBodyBytes = 10; // Far fewer than declared.

        var ms = new MemoryStream();

        // Write a valid-looking length header.
        ms.Write(BitConverter.GetBytes(declaredBodyLength), 0, 4);

        // Write only a partial body, then close the stream.
        var partialBody = new byte[actualBodyBytes];
        Array.Fill(partialBody, (byte)'{'); // Filler — will never be parsed.
        ms.Write(partialBody, 0, actualBodyBytes);

        // Rewind so FramedJsonTransport reads from the beginning.
        ms.Seek(0, SeekOrigin.Begin);

        var transport = new FramedJsonTransport(ms);

        // ReceiveAsync must throw EndOfStreamException, not hang or return null.
        Assert.ThrowsAsync<EndOfStreamException>(async () =>
        {
            await transport.ReceiveAsync<PipeRequest>(CancellationToken.None);
        }, "Truncated frame body must raise EndOfStreamException, not silently return null or hang.");
    }

    /// <summary>
    /// Writes only 2 bytes of the 4-byte length prefix, then closes the stream.
    /// Simulates a peer dying right after the frame boundary.
    /// Expected: <see cref="EndOfStreamException"/> from the header read loop.
    /// </summary>
    [Test]
    public void TruncatedFrame_HeaderIncomplete_ThrowsEndOfStream()
    {
        var ms = new MemoryStream();

        // Write only 2 of 4 length-prefix bytes.
        ms.Write(new byte[] { 0x10, 0x00 }, 0, 2);

        ms.Seek(0, SeekOrigin.Begin);

        var transport = new FramedJsonTransport(ms);

        Assert.ThrowsAsync<EndOfStreamException>(async () =>
        {
            await transport.ReceiveAsync<PipeRequest>(CancellationToken.None);
        }, "Truncated length prefix must raise EndOfStreamException.");
    }
}
