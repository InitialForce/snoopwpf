namespace SnoopWPF.Agent.Tests;

using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Protocol;
using SnoopWPF.Agent.Remote;

/// <summary>
/// Tests for <see cref="FramedJsonTransport"/>: framing correctness, max-frame enforcement,
/// cancel frame processing, and malformed message handling.
/// Uses in-process <see cref="NamedPipeServerStream"/>/<see cref="NamedPipeClientStream"/> pairs.
/// </summary>
[TestFixture]
public sealed class PipeTransportTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string UniquePipeName() => $"SnpTest_{Guid.NewGuid():N}";

    /// <summary>
    /// Creates a connected in-process server/client pipe pair.
    /// </summary>
    private static (NamedPipeServerStream Server, NamedPipeClientStream Client) CreatePipePair(string name)
    {
        var server = new NamedPipeServerStream(
            name,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var client = new NamedPipeClientStream(
            ".",
            name,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        // Connect synchronously for test simplicity.
        var connectTask = Task.Run(() => server.WaitForConnectionAsync());
        client.Connect(timeout: 5000);
        connectTask.GetAwaiter().GetResult();

        return (server, client);
    }

    // -------------------------------------------------------------------------
    // Framing correctness
    // -------------------------------------------------------------------------

    [Test]
    public async Task SendAndReceive_RoundTripsPipeRequest()
    {
        var (server, client) = CreatePipePair(UniquePipeName());
        await using (server)
        await using (client)
        {
            var senderTransport = new FramedJsonTransport(server);
            var receiverTransport = new FramedJsonTransport(client);

            var original = new PipeRequest { Id = 42, Method = "GetWindows", ParamsJson = "{\"includeHidden\":false}" };
            await senderTransport.SendAsync(original, CancellationToken.None);

            var received = await receiverTransport.ReceiveAsync<PipeRequest>(CancellationToken.None);

            Assert.That(received, Is.Not.Null);
            Assert.That(received!.Id, Is.EqualTo(42));
            Assert.That(received.Method, Is.EqualTo("GetWindows"));
            Assert.That(received.ParamsJson, Is.EqualTo("{\"includeHidden\":false}"));
        }
    }

    [Test]
    public async Task SendAndReceive_RoundTripsPipeResponse()
    {
        var (server, client) = CreatePipePair(UniquePipeName());
        await using (server)
        await using (client)
        {
            var senderTransport = new FramedJsonTransport(client);
            var receiverTransport = new FramedJsonTransport(server);

            var original = new PipeResponse { Id = 7, ResultJson = "{\"processName\":\"TestApp\",\"pid\":1234}" };
            await senderTransport.SendAsync(original, CancellationToken.None);

            var received = await receiverTransport.ReceiveAsync<PipeResponse>(CancellationToken.None);

            Assert.That(received, Is.Not.Null);
            Assert.That(received!.Id, Is.EqualTo(7));
            Assert.That(received.ResultJson, Does.Contain("\"processName\""));
            Assert.That(received.Error, Is.Null);
        }
    }

    [Test]
    public async Task SendAndReceive_ErrorResponse_PreservesErrorPayload()
    {
        var (server, client) = CreatePipePair(UniquePipeName());
        await using (server)
        await using (client)
        {
            var senderTransport = new FramedJsonTransport(client);
            var receiverTransport = new FramedJsonTransport(server);

            var original = new PipeResponse
            {
                Id = 99,
                ResultJson = null,
                Error = new PipeErrorPayload
                {
                    Code = "NodeNotFound",
                    Message = "Node 0:42 not found",
                    Suggestion = SnoopSuggestions.NodeNotFound,
                },
            };

            await senderTransport.SendAsync(original, CancellationToken.None);
            var received = await receiverTransport.ReceiveAsync<PipeResponse>(CancellationToken.None);

            Assert.That(received, Is.Not.Null);
            Assert.That(received!.Error, Is.Not.Null);
            Assert.That(received.Error!.Code, Is.EqualTo("NodeNotFound"));
            Assert.That(received.Error.Suggestion, Is.EqualTo(SnoopSuggestions.NodeNotFound));
        }
    }

    // -------------------------------------------------------------------------
    // Cancel frame
    // -------------------------------------------------------------------------

    [Test]
    public async Task SendAndReceive_CancelPayload_RoundTrips()
    {
        var (server, client) = CreatePipePair(UniquePipeName());
        await using (server)
        await using (client)
        {
            var senderTransport = new FramedJsonTransport(server);
            var receiverTransport = new FramedJsonTransport(client);

            var cancel = new PipeCancelPayload { Id = 5, Cancel = true };
            await senderTransport.SendAsync(cancel, CancellationToken.None);

            var received = await receiverTransport.ReceiveAsync<PipeCancelPayload>(CancellationToken.None);

            Assert.That(received, Is.Not.Null);
            Assert.That(received!.Id, Is.EqualTo(5));
            Assert.That(received.Cancel, Is.True);
        }
    }

    // -------------------------------------------------------------------------
    // Max frame enforcement
    // -------------------------------------------------------------------------

    [Test]
    public void SendAsync_FrameExceedsMaxSize_ThrowsInvalidOperation()
    {
        using var ms = new MemoryStream();
        var transport = new FramedJsonTransport(ms);

        // Build an object whose JSON exceeds 10 MiB.
        var big = new { Data = new string('x', ProtocolConstants.MaxFrameSize + 1) };

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await transport.SendAsync(big, CancellationToken.None);
        });
    }

    [Test]
    public void ReceiveAsync_OversizedFrameLength_ThrowsInvalidOperation()
    {
        using var ms = new MemoryStream();

        int oversized = ProtocolConstants.MaxFrameSize + 1;
        ms.Write(BitConverter.GetBytes(oversized), 0, 4);
        ms.Seek(0, SeekOrigin.Begin);

        var transport = new FramedJsonTransport(ms);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await transport.ReceiveAsync<PipeRequest>(CancellationToken.None);
        });
    }

    // -------------------------------------------------------------------------
    // Malformed message handling
    // -------------------------------------------------------------------------

    [Test]
    public void ReceiveAsync_MalformedJson_ThrowsInvalidOperation()
    {
        using var ms = new MemoryStream();

        byte[] badJson = System.Text.Encoding.UTF8.GetBytes("not valid json !!!");
        byte[] lengthBytes = BitConverter.GetBytes(badJson.Length);
        ms.Write(lengthBytes, 0, 4);
        ms.Write(badJson, 0, badJson.Length);
        ms.Seek(0, SeekOrigin.Begin);

        var transport = new FramedJsonTransport(ms);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await transport.ReceiveAsync<PipeRequest>(CancellationToken.None);
        });
    }

    [Test]
    public void ReceiveAsync_NegativeFrameLength_ThrowsInvalidOperation()
    {
        using var ms = new MemoryStream();

        ms.Write(BitConverter.GetBytes(-1), 0, 4);
        ms.Seek(0, SeekOrigin.Begin);

        var transport = new FramedJsonTransport(ms);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await transport.ReceiveAsync<PipeRequest>(CancellationToken.None);
        });
    }

    // -------------------------------------------------------------------------
    // EOF handling
    // -------------------------------------------------------------------------

    [Test]
    public async Task ReceiveAsync_EmptyStream_ReturnsNull()
    {
        using var ms = new MemoryStream(Array.Empty<byte>());
        var transport = new FramedJsonTransport(ms);

        // Clean EOF returns default (null for reference types).
        var result = await transport.ReceiveAsync<PipeRequest>(CancellationToken.None);
        Assert.That(result, Is.Null);
    }

    // -------------------------------------------------------------------------
    // Handshake protocol types
    // -------------------------------------------------------------------------

    [Test]
    public async Task Handshake_ChallengeAndResponse_RoundTrip()
    {
        var (server, client) = CreatePipePair(UniquePipeName());
        await using (server)
        await using (client)
        {
            var hostTransport = new FramedJsonTransport(server);
            var agentTransport = new FramedJsonTransport(client);

            // Host sends challenge.
            var challenge = new HandshakeChallenge
            {
                SessionToken = "tok-abc-123",
                ProtocolVersion = ProtocolConstants.ProtocolVersion,
            };
            await hostTransport.SendAsync(challenge, CancellationToken.None);

            // Agent reads challenge and sends response.
            var receivedChallenge = await agentTransport.ReceiveAsync<HandshakeChallenge>(CancellationToken.None);
            Assert.That(receivedChallenge!.SessionToken, Is.EqualTo("tok-abc-123"));
            Assert.That(receivedChallenge.ProtocolVersion, Is.EqualTo(ProtocolConstants.ProtocolVersion));

            var response = new HandshakeResponse
            {
                ProtocolVersion = ProtocolConstants.ProtocolVersion,
                AgentVersion = "1.0.0",
                TargetRuntime = "net8.0",
                SessionToken = receivedChallenge.SessionToken, // echo back
            };
            await agentTransport.SendAsync(response, CancellationToken.None);

            // Host reads response.
            var receivedResponse = await hostTransport.ReceiveAsync<HandshakeResponse>(CancellationToken.None);
            Assert.That(receivedResponse!.ProtocolVersion, Is.EqualTo(ProtocolConstants.ProtocolVersion));
            Assert.That(receivedResponse.SessionToken, Is.EqualTo("tok-abc-123"));
            Assert.That(receivedResponse.AgentVersion, Is.EqualTo("1.0.0"));
        }
    }

    // -------------------------------------------------------------------------
    // SerializeJson / DeserializeJson round-trip
    // -------------------------------------------------------------------------

    [Test]
    public void SerializeDeserializeJson_RoundTrips()
    {
        var original = new { nodeId = "0:42", treeType = "Visual", take = 100 };
        string json = FramedJsonTransport.SerializeJson(original);

        Assert.That(json, Does.Contain("nodeId"));
        Assert.That(json, Does.Contain("0:42"));

        // Deserialize back via anonymous-type helper won't work with STJ (no parameterless ctor),
        // so deserialise into a JsonDocument as a sanity check.
        using var doc = JsonDocument.Parse(json);
        Assert.That(doc.RootElement.GetProperty("nodeId").GetString(), Is.EqualTo("0:42"));
        Assert.That(doc.RootElement.GetProperty("take").GetInt32(), Is.EqualTo(100));
    }
}
