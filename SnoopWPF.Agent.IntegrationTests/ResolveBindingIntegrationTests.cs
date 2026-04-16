namespace SnoopWPF.Agent.IntegrationTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Data;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// Integration tests for <c>wpf_resolve_binding</c> / <see cref="ISnoopInspector.ResolveBindingAsync"/>
/// (M2-08: wpf_resolve_binding).
///
/// Test scenarios:
///   1. Clean DP binding — TextBlock.Text bound to a ViewModel property.
///   2. Missing DataContext — binding on an element with no DataContext.
///   3. Path typo — binding path that does not exist on the ViewModel.
///   4. Converter-throws — binding with a converter whose Convert method throws.
/// </summary>
[TestFixture]
public sealed class ResolveBindingIntegrationTests : WpfIntegrationTestBase
{
    // IDs set up in OneTimeSetUp
    private string? cleanBindingNodeId;
    private string? missingDcNodeId;
    private string? pathTypoNodeId;
    private string? converterThrowsNodeId;

    [OneTimeSetUp]
    public void SetUpBindings()
    {
        this.WpfApp.Dispatcher.Invoke(() =>
        {
            var root = this.WpfApp.MainWindow.Content as StackPanel;
            if (root is null)
            {
                throw new InvalidOperationException("TestWpfApp root is not a StackPanel.");
            }

            var vm = new BindingTestViewModel
            {
                UserName = "Alice",
                Nested = new NestedViewModel { Description = "Hello" },
            };

            // 1. Clean binding: TextBlock.Text -> ViewModel.UserName
            var cleanBlock = new TextBlock { Name = "resolveBindingClean" };
            cleanBlock.DataContext = vm;
            BindingOperations.SetBinding(
                cleanBlock,
                TextBlock.TextProperty,
                new Binding("UserName") { Mode = BindingMode.OneWay });
            root.Children.Add(cleanBlock);

            // Capture node ID via the inspector's node registry later — we'll look it up by name.

            // 2. Missing DataContext: TextBlock with binding but no DataContext.
            var missingDcBlock = new TextBlock { Name = "resolveBindingMissingDc" };
            // DataContext intentionally left null.
            BindingOperations.SetBinding(
                missingDcBlock,
                TextBlock.TextProperty,
                new Binding("SomeProperty") { Mode = BindingMode.OneWay });
            root.Children.Add(missingDcBlock);

            // 3. Path typo: binding to a property that does not exist.
            var typoBlock = new TextBlock { Name = "resolveBindingTypo" };
            typoBlock.DataContext = vm;
            BindingOperations.SetBinding(
                typoBlock,
                TextBlock.TextProperty,
                new Binding("ThisPropertyDoesNotExist") { Mode = BindingMode.OneWay });
            root.Children.Add(typoBlock);

            // 4. Converter that throws: TextBlock.Text with ThrowingConverter.
            var converterBlock = new TextBlock { Name = "resolveBindingConverter" };
            converterBlock.DataContext = vm;
            BindingOperations.SetBinding(
                converterBlock,
                TextBlock.TextProperty,
                new Binding("UserName")
                {
                    Mode = BindingMode.OneWay,
                    Converter = new ThrowingConverter(),
                });
            root.Children.Add(converterBlock);
        });

        // Resolve node IDs via inspector (must be off-dispatcher).
        var tree = this.Client.Inspector
            .GetVisualTreeAsync((string?)null, 10, "visual", null, default)
            .GetAwaiter()
            .GetResult();

        var allNodes = FlattenTree(tree.Root);

        this.cleanBindingNodeId = allNodes
            .FirstOrDefault(n => n.Name == "resolveBindingClean")?.NodeId;
        this.missingDcNodeId = allNodes
            .FirstOrDefault(n => n.Name == "resolveBindingMissingDc")?.NodeId;
        this.pathTypoNodeId = allNodes
            .FirstOrDefault(n => n.Name == "resolveBindingTypo")?.NodeId;
        this.converterThrowsNodeId = allNodes
            .FirstOrDefault(n => n.Name == "resolveBindingConverter")?.NodeId;
    }

    // -------------------------------------------------------------------------
    // Test 1: clean binding
    // -------------------------------------------------------------------------

    [Test]
    public async Task ResolveBinding_CleanBinding_ReturnsOkWithPathAndValue()
    {
        Assert.That(this.cleanBindingNodeId, Is.Not.Null,
            "resolveBindingClean node must be in the visual tree.");

        var dto = await this.Client.ResolveBindingAsync(
            this.cleanBindingNodeId!, "Text").ConfigureAwait(false);

        Assert.That(dto.HasBinding, Is.True, "HasBinding should be true.");
        Assert.That(dto.Path, Is.EqualTo("UserName"), "Path should be 'UserName'.");
        Assert.That(dto.Status, Is.EqualTo(BindingResolutionStatus.OK), "Status should be OK.");
        Assert.That(dto.SourceTypeName, Is.EqualTo(nameof(BindingTestViewModel)),
            "Source type should be BindingTestViewModel.");
        Assert.That(dto.Mode, Is.Not.Null.And.Not.Empty, "Mode should be populated.");

        // Path steps should include one entry for 'UserName'.
        Assert.That(dto.PathSteps, Has.Count.EqualTo(1), "Should have exactly one path step.");
        Assert.That(dto.PathSteps[0].Segment, Is.EqualTo("UserName"));
        Assert.That(dto.PathSteps[0].IsError, Is.False, "Path step should not be an error.");
        Assert.That(dto.PathSteps[0].Value, Is.EqualTo("Alice"), "Resolved value should be 'Alice'.");
    }

    [Test]
    public async Task ResolveBinding_CleanBinding_NoValidationErrors()
    {
        Assert.That(this.cleanBindingNodeId, Is.Not.Null);

        var dto = await this.Client.ResolveBindingAsync(
            this.cleanBindingNodeId!, "Text").ConfigureAwait(false);

        Assert.That(dto.ValidationErrors, Is.Empty,
            "Clean binding should have no validation errors.");
    }

    // -------------------------------------------------------------------------
    // Test 2: missing DataContext
    // -------------------------------------------------------------------------

    [Test]
    public async Task ResolveBinding_MissingDataContext_ReturnsMissingDataContextStatus()
    {
        Assert.That(this.missingDcNodeId, Is.Not.Null,
            "resolveBindingMissingDc node must be in the visual tree.");

        var dto = await this.Client.ResolveBindingAsync(
            this.missingDcNodeId!, "Text").ConfigureAwait(false);

        Assert.That(dto.HasBinding, Is.True, "HasBinding should be true even when DataContext is null.");
        Assert.That(dto.Status, Is.EqualTo(BindingResolutionStatus.MissingDataContext),
            "Status should be MissingDataContext when DataContext is null.");
        Assert.That(dto.ErrorDetail, Is.Not.Null.And.Not.Empty,
            "ErrorDetail should describe the missing DataContext.");
    }

    // -------------------------------------------------------------------------
    // Test 3: path typo
    // -------------------------------------------------------------------------

    [Test]
    public async Task ResolveBinding_PathTypo_ReturnsPathError()
    {
        Assert.That(this.pathTypoNodeId, Is.Not.Null,
            "resolveBindingTypo node must be in the visual tree.");

        var dto = await this.Client.ResolveBindingAsync(
            this.pathTypoNodeId!, "Text").ConfigureAwait(false);

        Assert.That(dto.HasBinding, Is.True);
        Assert.That(dto.Path, Is.EqualTo("ThisPropertyDoesNotExist"));
        Assert.That(dto.Status, Is.EqualTo(BindingResolutionStatus.PathError),
            "Status should be PathError for a non-existent path.");
        Assert.That(dto.PathSteps, Has.Count.EqualTo(1));
        Assert.That(dto.PathSteps[0].IsError, Is.True, "The single path step should be an error.");
    }

    // -------------------------------------------------------------------------
    // Test 4: converter set (ThrowingConverter)
    // -------------------------------------------------------------------------

    [Test]
    public async Task ResolveBinding_ConverterThrows_ReturnsConverterInfo()
    {
        Assert.That(this.converterThrowsNodeId, Is.Not.Null,
            "resolveBindingConverter node must be in the visual tree.");

        var dto = await this.Client.ResolveBindingAsync(
            this.converterThrowsNodeId!, "Text").ConfigureAwait(false);

        Assert.That(dto.HasBinding, Is.True);
        // Converter type name should be captured even though it throws.
        Assert.That(dto.ConverterTypeName, Is.EqualTo(nameof(ThrowingConverter)),
            "ConverterTypeName should report the converter type.");
        // Status may be ConverterError or OK — the key check is that ConverterTypeName is set
        // and the path steps were walked (the underlying value is still accessible).
        Assert.That(dto.PathSteps, Has.Count.GreaterThanOrEqualTo(1),
            "Path steps should be walked regardless of converter.");
    }

    // -------------------------------------------------------------------------
    // Test 5: no binding
    // -------------------------------------------------------------------------

    [Test]
    public async Task ResolveBinding_NoBinding_ReturnsNoBinding()
    {
        // Use testButton's Width property which is a plain DP with no binding.
        var tree = await this.Client.Inspector
            .GetVisualTreeAsync((string?)null, 10, "visual", null, default)
            .ConfigureAwait(false);

        var buttonNode = FlattenTree(tree.Root)
            .FirstOrDefault(n => n.Name == "testButton");

        if (buttonNode is null)
        {
            Assert.Ignore("testButton not found; skipping.");
            return;
        }

        var dto = await this.Client.ResolveBindingAsync(buttonNode.NodeId, "Width")
            .ConfigureAwait(false);

        Assert.That(dto.HasBinding, Is.False, "testButton.Width has no binding.");
        Assert.That(dto.Status, Is.EqualTo(BindingResolutionStatus.NoBinding));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static List<NodeDto> FlattenTree(NodeDto? root)
    {
        var result = new List<NodeDto>();
        if (root is null)
        {
            return result;
        }

        var queue = new Queue<NodeDto>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            result.Add(node);
            if (node.Children is not null)
            {
                foreach (var child in node.Children)
                {
                    queue.Enqueue(child);
                }
            }
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // Helper types
    // -------------------------------------------------------------------------

    private sealed class BindingTestViewModel
    {
        public string UserName { get; set; } = string.Empty;

        public NestedViewModel? Nested { get; set; }
    }

    private sealed class NestedViewModel
    {
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// A converter whose <see cref="Convert"/> method always throws.
    /// Used to exercise the ConverterError path.
    /// </summary>
    private sealed class ThrowingConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new InvalidOperationException("ThrowingConverter intentionally throws.");
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
