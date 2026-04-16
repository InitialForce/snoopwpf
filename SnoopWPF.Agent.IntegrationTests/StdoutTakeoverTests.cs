namespace SnoopWPF.Agent.IntegrationTests;

using System;
using System.IO;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Server;

/// <summary>
/// Regression tests for M1-19: <see cref="SnoopAgent.StartCoLocated"/> must redirect
/// <see cref="Console.Out"/> to <see cref="TextWriter.Null"/> as its very first statement,
/// so no application output can corrupt the MCP stdio transport's JSON-RPC framing.
/// </summary>
/// <remarks>
/// These tests call <see cref="SnoopAgent.StartCoLocated"/> directly.  Each test
/// must restore <see cref="Console.Out"/> in teardown so later tests are not affected.
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class StdoutTakeoverTests : WpfIntegrationTestBase
{
    private TextWriter? originalOut;

    /// <summary>Captures the real stdout before each test.</summary>
    [SetUp]
    public void SetUp()
    {
        this.originalOut = Console.Out;
    }

    /// <summary>
    /// Restores the real stdout and clears any active agent handle so the
    /// shared WPF fixture can be reused by subsequent tests.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        if (this.originalOut is not null)
        {
            Console.SetOut(this.originalOut);
            this.originalOut = null;
        }
    }

    /// <summary>
    /// After <see cref="SnoopAgent.StartCoLocated"/> returns, <see cref="Console.Out"/>
    /// must be <see cref="TextWriter.Null"/> — not the original stdout.
    /// </summary>
    [Test]
    public void StartCoLocated_RedirectsConsoleOut_ToNull()
    {
        SnoopAgentHandle? handle = null;
        try
        {
            this.WpfApp.Dispatcher.Invoke(() =>
            {
                handle = SnoopAgent.StartCoLocated(new SnoopAgentOptions
                {
                    Transport = TransportMode.Pipe,
                });
            });

            Assert.That(Console.Out, Is.SameAs(TextWriter.Null),
                "Console.Out must be TextWriter.Null after StartCoLocated() returns.");
        }
        finally
        {
            handle?.Dispose();
        }
    }

    /// <summary>
    /// After <see cref="SnoopAgent.StartCoLocated"/> returns, <see cref="Console.WriteLine"/>
    /// calls must not write to the original stdout stream.
    /// Regression guard for PRD §14 bugs 1-2: stray writes corrupt JSON-RPC framing.
    /// </summary>
    [Test]
    public void StartCoLocated_ConsoleWriteLine_DoesNotLeakToOriginalStdout()
    {
        // Arrange: replace the original stdout with a capturing writer so we can detect leaks.
        using var capturedOut = new StringWriter();
        Console.SetOut(capturedOut);
        this.originalOut = capturedOut; // TearDown will restore this

        // Also point the field to the real original so TearDown can restore properly.
        var trueOriginal = this.originalOut;

        SnoopAgentHandle? handle = null;
        try
        {
            this.WpfApp.Dispatcher.Invoke(() =>
            {
                handle = SnoopAgent.StartCoLocated(new SnoopAgentOptions
                {
                    Transport = TransportMode.Pipe,
                });
            });

            // Act: write to Console — must be swallowed by TextWriter.Null.
            Console.WriteLine("X");
            Console.Out.Flush();

            // Assert: the capturing writer must not have received "X".
            var leaked = capturedOut.ToString();
            Assert.That(leaked, Does.Not.Contain("X"),
                "Console.WriteLine(\"X\") must not reach the MCP transport stdout after StartCoLocated().");
        }
        finally
        {
            handle?.Dispose();
            // Restore to the true captured writer so TearDown sets Console.Out back correctly.
            Console.SetOut(trueOriginal ?? TextWriter.Null);
        }
    }
}
