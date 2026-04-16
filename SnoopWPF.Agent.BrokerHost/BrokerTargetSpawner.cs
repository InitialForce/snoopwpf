namespace SnoopWPF.Agent.BrokerHost;

using System;
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
    /// </summary>
    /// <param name="exe">Full path to the target executable.</param>
    /// <param name="args">Additional command-line arguments to pass to the target.</param>
    /// <param name="pipeName">The named-pipe name the target should connect to.</param>
    /// <param name="tokenHex">The hex-encoded session token for the handshake.</param>
    /// <returns>The started <see cref="Process"/>.</returns>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><see cref="ProcessStartInfo.UseShellExecute"/> is set to <see langword="false"/>.</item>
    ///   <item><see cref="ProcessStartInfo.CreateNoWindow"/> is set to <see langword="true"/>.</item>
    ///   <item><see cref="ProcessStartInfo.RedirectStandardInput"/> is set to <see langword="true"/>.</item>
    ///   <item><see cref="ProcessStartInfo.RedirectStandardOutput"/> is set to <see langword="true"/>.</item>
    ///   <item><see cref="ProcessStartInfo.RedirectStandardError"/> is set to <see langword="true"/>.</item>
    /// </list>
    /// The session token is passed to the child via a single-line JSON payload written to the
    /// child's stdin immediately after launch (see <see cref="BrokerHandshakePayload"/>), then
    /// the broker closes the stdin stream.  The token therefore never appears on the command line
    /// where it would be visible to other same-user processes via <c>GetCommandLine()</c> or
    /// <c>WMI Win32_Process.CommandLine</c>.
    ///
    /// Background drain tasks are started immediately after the process is launched.
    /// They read and discard all target stdout/stderr so the child pipe never fills and
    /// blocks, and so that no target output leaks onto the broker's stdio.
    /// </remarks>
    public static Process Spawn(string exe, string args, string pipeName, string tokenHex)
    {
        if (exe is null)
        {
            throw new ArgumentNullException(nameof(exe));
        }

        if (pipeName is null)
        {
            throw new ArgumentNullException(nameof(pipeName));
        }

        if (tokenHex is null)
        {
            throw new ArgumentNullException(nameof(tokenHex));
        }

        // Pass --snoop-pipe (not secret) on the command line.
        // The token is NOT included here; it is delivered via stdin below.
        string fullArgs = $"{args} --snoop-pipe={pipeName}".Trim();

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = fullArgs,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

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
