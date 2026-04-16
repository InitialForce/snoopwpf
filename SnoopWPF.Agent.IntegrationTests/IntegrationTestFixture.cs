namespace SnoopWPF.Agent.IntegrationTests;

using System;
using NUnit.Framework;

/// <summary>
/// Assembly-level NUnit setup fixture.
/// Creates a single shared <see cref="TestWpfApp"/> for the entire test run.
/// WPF allows only one <see cref="System.Windows.Application"/> per AppDomain, so
/// all integration tests must share this one instance.
/// </summary>
[SetUpFixture]
public sealed class IntegrationTestFixture
{
    private static TestWpfApp? sharedWpfApp;
    private static McpTestClient? sharedClient;

    /// <summary>
    /// Gets the shared <see cref="TestWpfApp"/> for the test run.
    /// Available only after <see cref="OneTimeSetUp"/> has run.
    /// </summary>
    public static TestWpfApp WpfApp => sharedWpfApp
        ?? throw new InvalidOperationException(
            "IntegrationTestFixture.WpfApp is not available. Did OneTimeSetUp run?");

    /// <summary>
    /// Gets the shared <see cref="McpTestClient"/> for the test run.
    /// Available only after <see cref="OneTimeSetUp"/> has run.
    /// </summary>
    public static McpTestClient Client => sharedClient
        ?? throw new InvalidOperationException(
            "IntegrationTestFixture.Client is not available. Did OneTimeSetUp run?");

    /// <summary>
    /// Creates the shared WPF application and MCP client once for the entire test run.
    /// </summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        sharedWpfApp = new TestWpfApp();
        sharedClient = new McpTestClient(sharedWpfApp);
    }

    /// <summary>
    /// Disposes the shared WPF application and MCP client after all tests complete.
    /// </summary>
    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        sharedClient?.Dispose();
        sharedClient = null;

        sharedWpfApp?.Dispose();
        sharedWpfApp = null;
    }
}
