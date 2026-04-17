namespace SnoopWPF.Agent.BrokerHost;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using SnoopWPF.Agent.Contracts.Protocol;

/// <summary>
/// Spawns a target process (e.g. <c>snoop-mcp.exe</c> in brokered mode) with the correct
/// pipe and token arguments, and ensures that the child's stdout/stderr are never forwarded
/// to the broker's own stdio (which is owned by the MCP transport).
/// </summary>
public static class BrokerTargetSpawner
{
    /// <summary>
    /// Starts the target executable with the given arguments and named-pipe handshake parameters.
    /// Prefer the <see cref="IReadOnlyList{String}"/> overload for safety; the string overload
    /// is retained for backward compatibility.
    /// </summary>
    public static Process Spawn(string exe, string args, string pipeName, string tokenHex)
    {
        // FX2-C6 (ADV-C3): the string-concatenation path is a shell-arg injection vector
        // when callers forward attacker-controlled text. Route through the safe overload
        // by splitting into argv-shaped tokens. Callers that already produce an argv list
        // should migrate to the overload below.
        var argv = new List<string>();
        if (!string.IsNullOrWhiteSpace(args))
        {
            // Single opaque string — delegate quoting behaviour to the OS loader by
            // passing it as one argument. Individual tokens with spaces would need to
            // use the argv overload; this string form is best-effort only.
            argv.Add(args);
        }

        return Spawn(exe, argv, pipeName, tokenHex);
    }

    /// <summary>
    /// Starts the target executable using an argv-shaped list. Each element of
    /// <paramref name="args"/> becomes a single argument without any shell or command-line
    /// parsing — this eliminates the injection surface of string concatenation.
    /// </summary>
    /// <param name="exe">Full path to the target executable.</param>
    /// <param name="args">Additional command-line arguments (one per list entry).</param>
    /// <param name="pipeName">The named-pipe name the target should connect to.</param>
    /// <param name="tokenHex">The hex-encoded session token for the handshake.</param>
    /// <returns>The started <see cref="Process"/>.</returns>
    /// <remarks>
    /// The session token is passed via a single-line JSON payload on stdin (see
    /// <see cref="BrokerHandshakePayload"/>) and never appears on the command line.
    /// </remarks>
    public static Process Spawn(string exe, IReadOnlyList<string> args, string pipeName, string tokenHex)
    {
        if (exe is null)
        {
            throw new ArgumentNullException(nameof(exe));
        }

        if (args is null)
        {
            throw new ArgumentNullException(nameof(args));
        }

        if (pipeName is null)
        {
            throw new ArgumentNullException(nameof(pipeName));
        }

        if (tokenHex is null)
        {
            throw new ArgumentNullException(nameof(tokenHex));
        }

        // FX2-C6: use ArgumentList so each arg is escaped individually by the CLR.
        // CommandLineToArgvW-based string parsing is bypassed; an attacker-controlled
        // arg cannot inject additional flags.
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var a in args)
        {
            if (a is null)
            {
                continue;
            }

            psi.ArgumentList.Add(a);
        }

        // The only broker-controlled flag. Pipe name is validated upstream
        // (BrokerHost.Start throws on empty) and consists of filesystem-safe characters.
        psi.ArgumentList.Add("--snoop-pipe=" + pipeName);

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Process.Start returned null for '{exe}'.");

        // Write the handshake payload (pipe name + token) to the child's stdin, then close it.
        // The child reads this single line during startup and forgets it.
        // Closing stdin signals EOF so the child's ReadLine call returns.
        // The write is best-effort: if the child exits before reading, the pipe may already be
        // broken. That is not an error — the child simply ignored the handshake (e.g. fast exit).
        var payload = new BrokerHandshakePayload { Pipe = pipeName, Token = tokenHex };
        string payloadJson = JsonSerializer.Serialize(payload);
        try
        {
            process.StandardInput.WriteLine(payloadJson);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // Child exited before reading stdin — pipe already closed. Not an error.
        }

        // Drain stdout and stderr on background tasks.
        // These tasks read and discard output so the child pipe never fills and blocks,
        // and so that no target output reaches the broker's stdio.
        _ = DrainStreamAsync(process.StandardOutput);
        _ = DrainStreamAsync(process.StandardError);

        return process;
    }

    private static async Task DrainStreamAsync(StreamReader reader)
    {
        try
        {
            // Read and discard all output; loop until EOF.
            char[] buffer = new char[4096];
            while (true)
            {
                int read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
            }
        }
        catch
        {
            // Best-effort drain — ignore all errors (process may have already exited).
        }
    }
}
