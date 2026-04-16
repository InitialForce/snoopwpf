namespace SnoopWPF.Agent.BrokerHost;

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

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
    ///   <item><see cref="ProcessStartInfo.RedirectStandardOutput"/> is set to <see langword="true"/>.</item>
    ///   <item><see cref="ProcessStartInfo.RedirectStandardError"/> is set to <see langword="true"/>.</item>
    /// </list>
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

        string fullArgs = $"{args} --snoop-pipe={pipeName} --snoop-token={tokenHex}".Trim();

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = fullArgs,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Process.Start returned null for '{exe}'.");

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
