namespace SnoopWPF.Agent.IntegrationTests;

using NUnit.Framework;

/// <summary>
/// Base class for integration tests that require a live WPF application.
/// Uses the shared <see cref="IntegrationTestFixture.WpfApp"/> singleton because
/// WPF allows only one <see cref="System.Windows.Application"/> per AppDomain.
///
/// All derived fixtures are marked <c>[NonParallelizable]</c> to prevent concurrent
/// access to the shared application state.
/// </summary>
/// <remarks>
/// Tests derived from this class are tagged <c>[Category("RequiresWpf")]</c> and
/// can be excluded in headless CI with:
///   dotnet test --filter "Category!=RequiresWpf"
/// </remarks>
[Category("RequiresWpf")]
[NonParallelizable]
public abstract class WpfIntegrationTestBase
{
    /// <summary>Gets the shared <see cref="McpTestClient"/> for this test.</summary>
    protected McpTestClient Client => IntegrationTestFixture.Client;

    /// <summary>Gets the shared <see cref="TestWpfApp"/> for this test.</summary>
    protected TestWpfApp WpfApp => IntegrationTestFixture.WpfApp;
}
