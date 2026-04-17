namespace SnoopWPF.Agent.BrokerHost;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
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
    /// <param name="args">
    /// A Windows command-line argument string (may contain quoted tokens with embedded spaces,
    /// e.g. <c>--mode test --file "C:\x y.txt"</c>). Parsed with <c>CommandLineToArgvW</c>
    /// so each logical token is passed as a separate argv entry. Must not contain attacker-controlled
    /// text — use the <see cref="Spawn(string, IReadOnlyList{string}, string, string)"/> overload
    /// for any caller that already has an argv list.
    /// </param>
    /// <param name="pipeName">The named-pipe name the target should connect to.</param>
    /// <param name="tokenHex">The hex-encoded session token for the handshake.</param>
    /// <returns>The started <see cref="Process"/>.</returns>
    /// <remarks>
    /// <para>
    /// This overload is kept for source compatibility with callers that produce a compound
    /// command-line string. New callers should use the
    /// <see cref="Spawn(string, IReadOnlyList{string}, string, string)"/> overload, which
    /// eliminates any parsing ambiguity.
    /// </para>
    /// <para>
    /// <b>FX4-C6-retry:</b> the previous implementation passed the entire <paramref name="args"/>
    /// string as a single opaque argv item, silently breaking callers that passed compound strings
    /// like <c>--mode test --file "C:\path with spaces\f.txt"</c> (child received one item, not four).
    /// This revision parses via <c>CommandLineToArgvW</c> — the canonical Windows command-line
    /// parser — so behaviour matches what <c>cmd.exe</c> would produce.
    /// </para>
    /// </remarks>
    [Obsolete(
        "Pass IReadOnlyList<string> to preserve argv boundaries. " +
        "This overload is kept for source-compat only and will throw on non-empty args in a future release.",
        error: false)]
    public static Process Spawn(string exe, string args, string pipeName, string tokenHex)
    {
        // FX4-C6-retry: parse the compound string into individual argv tokens using the
        // canonical Windows parser (CommandLineToArgvW). This restores correct behaviour
        // for callers that passed e.g. "--mode test --file \"C:\\x y.txt\"" and expected
        // four separate argv entries — not one opaque blob.
        var argv = new List<string>();
        if (!string.IsNullOrEmpty(args))
        {
            argv.AddRange(SplitCommandLine(args));
        }

        return Spawn(exe, argv, pipeName, tokenHex);
    }

    /// <summary>
    /// Splits a Windows command-line argument string into individual argv tokens using
    /// <c>CommandLineToArgvW</c> from <c>shell32.dll</c> — the same parser that
    /// <c>cmd.exe</c> and the Windows loader use.
    /// </summary>
    /// <param name="commandLine">The command-line string to parse (not including the program name).</param>
    /// <returns>An ordered list of unquoted argument strings.</returns>
    /// <exception cref="System.ComponentModel.Win32Exception">
    /// Thrown if <c>CommandLineToArgvW</c> returns a null pointer (unexpected; indicates
    /// an out-of-memory condition or severely malformed input).
    /// </exception>
    private static IReadOnlyList<string> SplitCommandLine(string commandLine)
    {
        if (string.IsNullOrEmpty(commandLine))
        {
            return Array.Empty<string>();
        }

        nint argvPtr = NativeMethods.CommandLineToArgvW(commandLine, out int argc);
        if (argvPtr == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(),
                $"CommandLineToArgvW failed to parse: {commandLine}");
        }

        try
        {
            var result = new string[argc];
            for (int i = 0; i < argc; i++)
            {
                nint strPtr = Marshal.ReadIntPtr(argvPtr, i * IntPtr.Size);
                result[i] = Marshal.PtrToStringUni(strPtr) ?? string.Empty;
            }

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(argvPtr);
        }
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

/// <summary>
/// Thin P/Invoke shim for the Windows shell APIs used by <see cref="BrokerTargetSpawner"/>.
/// </summary>
internal static class NativeMethods
{
    /// <summary>
    /// Parses a Unicode command-line string into an array of pointers to argument strings,
    /// using the same rules as the Windows command-line parser.
    /// </summary>
    /// <param name="lpCmdLine">The command-line string to parse. Must not include the program name.</param>
    /// <param name="pNumArgs">Receives the number of arguments in the returned array.</param>
    /// <returns>
    /// A pointer to an array of <paramref name="pNumArgs"/> <c>LPWSTR</c> pointers allocated in
    /// a single <c>LocalAlloc</c> block. The caller must free this pointer with
    /// <see cref="Marshal.FreeHGlobal"/> when done. Returns <c>IntPtr.Zero</c> on failure;
    /// call <see cref="Marshal.GetLastWin32Error"/> for the error code.
    /// </returns>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CommandLineToArgvW(
        [MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine,
        out int pNumArgs);
}
