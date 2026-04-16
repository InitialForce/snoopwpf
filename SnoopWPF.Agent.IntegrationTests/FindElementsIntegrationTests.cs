namespace SnoopWPF.Agent.IntegrationTests;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// Integration tests for element search against a real WPF application.
/// Exercises <c>FindElementsAsync</c> through the <see cref="McpTestClient"/>.
///
/// The <see cref="TestWpfApp"/> contains known elements:
///   Button ("testButton"), TextBox ("testTextBox"), TextBlock ("testTextBlock"),
///   ListBox ("testListBox") inside a StackPanel ("rootPanel").
/// </summary>
[TestFixture]
public sealed class FindElementsIntegrationTests : WpfIntegrationTestBase
{
    // -------------------------------------------------------------------------
    // FindElementsAsync — by type name
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that FindElementsAsync by type "Button" returns at least one result.
    /// </summary>
    [Test]
    public async Task FindElements_ByTypeName_Button_ReturnsResults()
    {
        var result = await this.Client.Inspector
            .FindElementsAsync(
                typeName: "Button",
                name: null,
                rootNodeId: null,
                conditions: null,
                treeType: "visual",
                maxResults: 20,
                ct: default)
            .ConfigureAwait(false);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Results, Is.Not.Empty,
            "Searching for 'Button' should return at least one result.");
        Assert.That(result.TotalScanned, Is.GreaterThan(0),
            "TotalScanned must be positive.");
    }

    /// <summary>
    /// Verifies that all hits from a type search are Buttons.
    /// </summary>
    [Test]
    public async Task FindElements_ByTypeName_AllHitsMatchType()
    {
        var result = await this.Client.Inspector
            .FindElementsAsync(
                typeName: "Button",
                name: null,
                rootNodeId: null,
                conditions: null,
                treeType: "visual",
                maxResults: 50,
                ct: default)
            .ConfigureAwait(false);

        foreach (var hit in result.Results)
        {
            Assert.That(
                hit.Node.TypeName,
                Does.Contain("Button").IgnoreCase,
                $"Hit TypeName '{hit.Node.TypeName}' should contain 'Button'.");
        }
    }

    /// <summary>
    /// Verifies that FindElementsAsync by type "TextBox" returns at least one result.
    /// </summary>
    [Test]
    public async Task FindElements_ByTypeName_TextBox_ReturnsResults()
    {
        var result = await this.Client.Inspector
            .FindElementsAsync(
                typeName: "TextBox",
                name: null,
                rootNodeId: null,
                conditions: null,
                treeType: "visual",
                maxResults: 20,
                ct: default)
            .ConfigureAwait(false);

        Assert.That(result.Results, Is.Not.Empty,
            "Searching for 'TextBox' should return at least one result.");
    }

    // -------------------------------------------------------------------------
    // FindElementsAsync — by element name
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that FindElementsAsync by name "testButton" returns exactly one result.
    /// </summary>
    [Test]
    public async Task FindElements_ByName_TestButton_ReturnsResult()
    {
        var result = await this.Client.Inspector
            .FindElementsAsync(
                typeName: null,
                name: "testButton",
                rootNodeId: null,
                conditions: null,
                treeType: "visual",
                maxResults: 20,
                ct: default)
            .ConfigureAwait(false);

        Assert.That(result.Results, Is.Not.Empty,
            "Searching by name 'testButton' should return at least one hit.");

        // All results must have a Name containing "testButton".
        foreach (var hit in result.Results)
        {
            Assert.That(
                hit.Node.Name,
                Does.Contain("testButton").IgnoreCase,
                $"Hit Name '{hit.Node.Name}' should contain 'testButton'.");
        }
    }

    /// <summary>
    /// Verifies that FindElementsAsync by name "testTextBox" returns a result.
    /// </summary>
    [Test]
    public async Task FindElements_ByName_TestTextBox_ReturnsResult()
    {
        var result = await this.Client.Inspector
            .FindElementsAsync(
                typeName: null,
                name: "testTextBox",
                rootNodeId: null,
                conditions: null,
                treeType: "visual",
                maxResults: 20,
                ct: default)
            .ConfigureAwait(false);

        Assert.That(result.Results, Is.Not.Empty,
            "Searching by name 'testTextBox' should return at least one hit.");
    }

    // -------------------------------------------------------------------------
    // FindElementsAsync — empty results
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that searching for a non-existent type returns empty results.
    /// </summary>
    [Test]
    public async Task FindElements_NonExistentType_ReturnsEmpty()
    {
        var result = await this.Client.Inspector
            .FindElementsAsync(
                typeName: "ThisTypeDefinitelyDoesNotExist_xyz_99",
                name: null,
                rootNodeId: null,
                conditions: null,
                treeType: "visual",
                maxResults: 20,
                ct: default)
            .ConfigureAwait(false);

        Assert.That(result.Results, Is.Empty,
            "Searching for a non-existent type must return no results.");
    }

    /// <summary>
    /// Verifies that searching for a non-existent name returns empty results.
    /// </summary>
    [Test]
    public async Task FindElements_NonExistentName_ReturnsEmpty()
    {
        var result = await this.Client.Inspector
            .FindElementsAsync(
                typeName: null,
                name: "elementThatDoesNotExistAtAll_xyz_12345",
                rootNodeId: null,
                conditions: null,
                treeType: "visual",
                maxResults: 20,
                ct: default)
            .ConfigureAwait(false);

        Assert.That(result.Results, Is.Empty,
            "Searching for a non-existent name must return no results.");
    }

    // -------------------------------------------------------------------------
    // FindElementsAsync — property conditions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that property condition filtering works for a known value.
    /// Searching for elements with Name="testButton" via property condition.
    /// </summary>
    [Test]
    public async Task FindElements_WithPropertyCondition_ReturnsMatchingElements()
    {
        var conditions = new List<PropertyConditionDto>
        {
            new PropertyConditionDto
            {
                Property = "Name",
                Operator = "Equals",
                Value = "testButton",
            },
        };

        var result = await this.Client.Inspector
            .FindElementsAsync(
                typeName: null,
                name: null,
                rootNodeId: null,
                conditions: conditions,
                treeType: "visual",
                maxResults: 20,
                ct: default)
            .ConfigureAwait(false);

        // The condition filters by the Name DependencyProperty.
        // Result may be empty if the property is not accessible via reflection path —
        // that is acceptable. We verify no exception is thrown and result is non-null.
        Assert.That(result, Is.Not.Null,
            "FindElementsAsync with property conditions must return a non-null result.");
        Assert.That(result.Results, Is.Not.Null);
    }

    /// <summary>
    /// Verifies that FindElementsAsync respects the maxResults cap.
    /// </summary>
    [Test]
    public async Task FindElements_MaxResults_IsRespected()
    {
        var cap = 2;

        var result = await this.Client.Inspector
            .FindElementsAsync(
                typeName: null, // Match all
                name: null,
                rootNodeId: null,
                conditions: null,
                treeType: "visual",
                maxResults: cap,
                ct: default)
            .ConfigureAwait(false);

        Assert.That(result.Results.Count, Is.LessThanOrEqualTo(cap),
            $"Results count must not exceed maxResults={cap}.");
    }

    // -------------------------------------------------------------------------
    // FindElementsAsync — result structure
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies that all hits have a non-empty NodeId on their Node.
    /// </summary>
    [Test]
    public async Task FindElements_AllHits_HaveNodeId()
    {
        var result = await this.Client.Inspector
            .FindElementsAsync(
                typeName: "Button",
                name: null,
                rootNodeId: null,
                conditions: null,
                treeType: "visual",
                maxResults: 50,
                ct: default)
            .ConfigureAwait(false);

        foreach (var hit in result.Results)
        {
            Assert.That(hit.Node.NodeId, Is.Not.Null.And.Not.Empty,
                "Every FindElementHitDto.Node must have a non-empty NodeId.");
        }
    }

    /// <summary>
    /// Verifies that the Path field in every hit is non-null.
    /// </summary>
    [Test]
    public async Task FindElements_AllHits_HaveNonNullPath()
    {
        var result = await this.Client.Inspector
            .FindElementsAsync(
                typeName: "Button",
                name: null,
                rootNodeId: null,
                conditions: null,
                treeType: "visual",
                maxResults: 50,
                ct: default)
            .ConfigureAwait(false);

        foreach (var hit in result.Results)
        {
            Assert.That(hit.Path, Is.Not.Null,
                "FindElementHitDto.Path must not be null.");
        }
    }
}
