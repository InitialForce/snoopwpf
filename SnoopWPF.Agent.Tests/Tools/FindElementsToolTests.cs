namespace SnoopWPF.Agent.Tests.Tools;

using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Tests.Fakes;
using SnoopWPF.Agent.Tools;

/// <summary>
/// Unit tests for <see cref="FindElementsTool"/>.
/// </summary>
[TestFixture]
public class FindElementsToolTests
{
    private FakeSnoopInspector fake = null!;

    private FindElementsTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new FindElementsTool(this.fake);
    }

    // ── Happy path ──────────────────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_ReturnsMatchedElements()
    {
        this.fake.OnFindElements = (typeName, name, rootNodeId, conditions, treeType, maxResults, ct) =>
            System.Threading.Tasks.Task.FromResult(new FindElementResultDto
            {
                Results = new List<FindElementHitDto>
                {
                    new FindElementHitDto
                    {
                        Node = new NodeDto
                        {
                            NodeId = "0:5",
                            TypeName = "Button",
                            DisplayName = "SaveButton",
                        },
                        Path = new List<string> { "0:1", "0:3", "0:5" },
                    },
                },
                TotalScanned = 200,
                Truncated = false,
            });

        var json = await this.tool.FindElementsAsync(typeName: "Button");

        var doc = JsonNode.Parse(json)!;
        var results = doc["results"]!.AsArray();
        Assert.That(results.Count, Is.EqualTo(1));
        Assert.That(results[0]!["node"]!["nodeId"]!.GetValue<string>(), Is.EqualTo("0:5"));
        Assert.That(results[0]!["node"]!["typeName"]!.GetValue<string>(), Is.EqualTo("Button"));
        Assert.That(doc["totalScanned"]!.GetValue<int>(), Is.EqualTo(200));
        Assert.That(doc["truncated"]!.GetValue<bool>(), Is.False);
    }

    [Test]
    public async Task HappyPath_TruncatedResult()
    {
        this.fake.OnFindElements = (_, _, _, _, _, _, _) =>
            System.Threading.Tasks.Task.FromResult(new FindElementResultDto
            {
                Results = new List<FindElementHitDto>(),
                TotalScanned = 5000,
                Truncated = true,
            });

        var json = await this.tool.FindElementsAsync(maxResults: 50);

        var doc = JsonNode.Parse(json)!;
        Assert.That(doc["truncated"]!.GetValue<bool>(), Is.True);
        Assert.That(doc["totalScanned"]!.GetValue<int>(), Is.EqualTo(5000));
    }

    // ── Parameter forwarding ────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsAllParameters_ToInspector()
    {
        string? capturedTypeName = "not-set";
        string? capturedName = "not-set";
        string? capturedRootNodeId = "not-set";
        List<PropertyConditionDto>? capturedConditions = null;
        string? capturedTreeType = "not-set";
        int capturedMaxResults = -1;

        this.fake.OnFindElements = (typeName, name, rootNodeId, conditions, treeType, maxResults, ct) =>
        {
            capturedTypeName = typeName;
            capturedName = name;
            capturedRootNodeId = rootNodeId;
            capturedConditions = conditions;
            capturedTreeType = treeType;
            capturedMaxResults = maxResults;
            return System.Threading.Tasks.Task.FromResult(new FindElementResultDto());
        };

        var conds = new List<PropertyConditionDto>
        {
            new PropertyConditionDto { Property = "IsEnabled", Operator = "Equals", Value = "True" },
        };

        await this.tool.FindElementsAsync(
            typeName: "TextBox",
            name: "SearchBox",
            rootNodeId: "0:3",
            conditions: conds,
            treeType: "logical",
            maxResults: 100);

        Assert.That(capturedTypeName, Is.EqualTo("TextBox"));
        Assert.That(capturedName, Is.EqualTo("SearchBox"));
        Assert.That(capturedRootNodeId, Is.EqualTo("0:3"));
        Assert.That(capturedConditions, Is.Not.Null);
        Assert.That(capturedConditions!.Count, Is.EqualTo(1));
        Assert.That(capturedConditions[0].Property, Is.EqualTo("IsEnabled"));
        Assert.That(capturedTreeType, Is.EqualTo("logical"));
        Assert.That(capturedMaxResults, Is.EqualTo(100));
    }

    [Test]
    public async Task DefaultParameters_NullTypeNameAndName_DefaultTreeTypeAndMaxResults()
    {
        string? capturedTypeName = "not-null";
        string? capturedName = "not-null";
        string? capturedTreeType = "not-set";
        int capturedMaxResults = -1;

        this.fake.OnFindElements = (typeName, name, rootNodeId, conditions, treeType, maxResults, ct) =>
        {
            capturedTypeName = typeName;
            capturedName = name;
            capturedTreeType = treeType;
            capturedMaxResults = maxResults;
            return System.Threading.Tasks.Task.FromResult(new FindElementResultDto());
        };

        await this.tool.FindElementsAsync();

        Assert.That(capturedTypeName, Is.Null);
        Assert.That(capturedName, Is.Null);
        Assert.That(capturedTreeType, Is.EqualTo("visual"));
        Assert.That(capturedMaxResults, Is.EqualTo(50));
    }

    // ── Filter combinations ───────────────────────────────────────────────────────

    [Test]
    public async Task FindByNameOnly_ForwardsNullTypeName()
    {
        string? capturedTypeName = "not-null";
        string? capturedName = null;

        this.fake.OnFindElements = (typeName, name, _, _, _, _, _) =>
        {
            capturedTypeName = typeName;
            capturedName = name;
            return System.Threading.Tasks.Task.FromResult(new FindElementResultDto());
        };

        await this.tool.FindElementsAsync(name: "MyButton");

        Assert.That(capturedTypeName, Is.Null);
        Assert.That(capturedName, Is.EqualTo("MyButton"));
    }

    [Test]
    public async Task FindWithPropertyConditions_ContainsOperator()
    {
        List<PropertyConditionDto>? capturedConditions = null;

        this.fake.OnFindElements = (_, _, _, conditions, _, _, _) =>
        {
            capturedConditions = conditions;
            return System.Threading.Tasks.Task.FromResult(new FindElementResultDto());
        };

        var conds = new List<PropertyConditionDto>
        {
            new PropertyConditionDto { Property = "Tag", Operator = "Contains", Value = "featured" },
        };

        await this.tool.FindElementsAsync(conditions: conds);

        Assert.That(capturedConditions, Is.Not.Null);
        Assert.That(capturedConditions![0].Operator, Is.EqualTo("Contains"));
    }

    // ── JSON camelCase ────────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnFindElements = (_, _, _, _, _, _, _) =>
            System.Threading.Tasks.Task.FromResult(new FindElementResultDto
            {
                Results = new List<FindElementHitDto>(),
                TotalScanned = 0,
                Truncated = false,
            });

        var json = await this.tool.FindElementsAsync();

        Assert.That(json, Does.Contain("\"results\""));
        Assert.That(json, Does.Contain("\"totalScanned\""));
        Assert.That(json, Does.Not.Contain("\"TotalScanned\""));
    }

    // ── Error mapping ────────────────────────────────────────────────────────────

    [Test]
    public void NodeNotFound_ThrowsMcpException()
    {
        this.fake.OnFindElements = (_, _, _, _, _, _, _) =>
            throw new SnoopException(SnoopErrorCode.NodeNotFound, "Root node not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.FindElementsAsync(rootNodeId: "0:99"));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    [Test]
    public void SessionNotFound_ThrowsMcpException()
    {
        this.fake.OnFindElements = (_, _, _, _, _, _, _) =>
            throw new SnoopException(SnoopErrorCode.SessionNotFound, "Session not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.FindElementsAsync());

        Assert.That(ex!.Message, Does.Contain("SESSION_NOT_FOUND"));
    }
}
