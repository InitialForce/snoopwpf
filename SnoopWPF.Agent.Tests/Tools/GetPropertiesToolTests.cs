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
/// BEAD-009: Tests for <see cref="GetPropertiesTool"/> — cursor-paginated properties with filter.
/// </summary>
[TestFixture]
public class GetPropertiesToolTests
{
    private FakeSnoopInspector fake = null!;

    private GetPropertiesTool tool = null!;

    [SetUp]
    public void SetUp()
    {
        this.fake = new FakeSnoopInspector();
        this.tool = new GetPropertiesTool(this.fake);
    }

    // ── Happy path ──────────────────────────────────────────────────────────────

    [Test]
    public async Task HappyPath_ReturnsPropertyPage()
    {
        this.fake.OnGetProperties = (nodeId, filter, category, includeDefaults, cursor, take, ct) =>
            Task.FromResult(new CursorPage<PropertyDto>
            {
                Items = new List<PropertyDto>
                {
                    new PropertyDto
                    {
                        Name = "Background",
                        TypeName = "Brush",
                        Value = "White",
                        ValueSource = "Local",
                        IsLocallySet = true,
                        IsDataBound = false,
                        HasBindingError = false,
                        IsReadOnly = false,
                        HasTypeConverter = true,
                        IsRedacted = false,
                    },
                    new PropertyDto
                    {
                        Name = "Password",
                        TypeName = "String",
                        Value = "[REDACTED]",
                        IsRedacted = true,
                    },
                },
                TotalCount = 2,
                HasMore = false,
            });

        var json = await this.tool.GetPropertiesAsync("0:1");

        var doc = JsonNode.Parse(json)!;
        var items = doc["items"]!.AsArray();
        Assert.That(items.Count, Is.EqualTo(2));

        var bg = items[0]!;
        Assert.That(bg["name"]!.GetValue<string>(), Is.EqualTo("Background"));
        Assert.That(bg["value"]!.GetValue<string>(), Is.EqualTo("White"));
        Assert.That(bg["isLocallySet"]!.GetValue<bool>(), Is.True);
        Assert.That(bg["isRedacted"]!.GetValue<bool>(), Is.False);

        var pwd = items[1]!;
        Assert.That(pwd["name"]!.GetValue<string>(), Is.EqualTo("Password"));
        Assert.That(pwd["isRedacted"]!.GetValue<bool>(), Is.True);
        Assert.That(pwd["value"]!.GetValue<string>(), Is.EqualTo("[REDACTED]"));
    }

    [Test]
    public async Task HappyPath_PropertyWithBindingError()
    {
        this.fake.OnGetProperties = (_, _, _, _, _, _, _) =>
            Task.FromResult(new CursorPage<PropertyDto>
            {
                Items = new List<PropertyDto>
                {
                    new PropertyDto
                    {
                        Name = "Text",
                        IsDataBound = true,
                        HasBindingError = true,
                        BindingError = "Cannot find source for binding",
                    },
                },
                TotalCount = 1,
            });

        var json = await this.tool.GetPropertiesAsync("0:3");

        var doc = JsonNode.Parse(json)!;
        var item = doc["items"]![0]!;
        Assert.That(item["isDataBound"]!.GetValue<bool>(), Is.True);
        Assert.That(item["hasBindingError"]!.GetValue<bool>(), Is.True);
        Assert.That(item["bindingError"]!.GetValue<string>(), Does.Contain("Cannot find source"));
    }

    // ── Parameter forwarding ────────────────────────────────────────────────────

    [Test]
    public async Task ForwardsFilter_ToInspector()
    {
        string? capturedFilter = "not-set";
        string? capturedCategory = "not-set";
        bool capturedIncludeDefaults = true;
        string? capturedCursor = "not-null";
        int capturedTake = -1;

        this.fake.OnGetProperties = (nodeId, filter, category, includeDefaults, cursor, take, ct) =>
        {
            capturedFilter = filter;
            capturedCategory = category;
            capturedIncludeDefaults = includeDefaults;
            capturedCursor = cursor;
            capturedTake = take;
            return Task.FromResult(new CursorPage<PropertyDto> { Items = new List<PropertyDto>() });
        };

        await this.tool.GetPropertiesAsync(
            nodeId: "0:1",
            filter: "back",
            category: "color",
            includeDefaults: true,
            cursor: "page2",
            take: 50);

        Assert.That(capturedFilter, Is.EqualTo("back"));
        Assert.That(capturedCategory, Is.EqualTo("color"));
        Assert.That(capturedIncludeDefaults, Is.True);
        Assert.That(capturedCursor, Is.EqualTo("page2"));
        Assert.That(capturedTake, Is.EqualTo(50));
    }

    [Test]
    public async Task DefaultParameters_FilterNull_CategoryNull_IncludeDefaultsFalse_Take100()
    {
        string? capturedFilter = "not-null";
        string? capturedCategory = "not-null";
        bool capturedIncludeDefaults = true;
        int capturedTake = -1;

        this.fake.OnGetProperties = (nodeId, filter, category, includeDefaults, cursor, take, ct) =>
        {
            capturedFilter = filter;
            capturedCategory = category;
            capturedIncludeDefaults = includeDefaults;
            capturedTake = take;
            return Task.FromResult(new CursorPage<PropertyDto> { Items = new List<PropertyDto>() });
        };

        await this.tool.GetPropertiesAsync("0:1");

        Assert.That(capturedFilter, Is.Null);
        Assert.That(capturedCategory, Is.Null);
        Assert.That(capturedIncludeDefaults, Is.False);
        Assert.That(capturedTake, Is.EqualTo(100));
    }

    // ── JSON camelCase ────────────────────────────────────────────────────────────

    [Test]
    public async Task JsonOutput_UsesCamelCaseKeys()
    {
        this.fake.OnGetProperties = (_, _, _, _, _, _, _) =>
            Task.FromResult(new CursorPage<PropertyDto>
            {
                Items = new List<PropertyDto>
                {
                    new PropertyDto { Name = "Foreground", IsDataBound = true },
                },
                HasMore = false,
                TotalCount = 1,
            });

        var json = await this.tool.GetPropertiesAsync("0:1");

        Assert.That(json, Does.Contain("\"isDataBound\""));
        Assert.That(json, Does.Contain("\"hasMore\""));
        Assert.That(json, Does.Not.Contain("\"IsDataBound\""));
    }

    // ── Error mapping ────────────────────────────────────────────────────────────

    [Test]
    public void NodeNotFound_ThrowsMcpException()
    {
        this.fake.OnGetProperties = (_, _, _, _, _, _, _) =>
            throw new SnoopException(SnoopErrorCode.NodeNotFound, "Node not found");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetPropertiesAsync("0:99"));

        Assert.That(ex!.Message, Does.Contain("NODE_NOT_FOUND"));
        Assert.That(ex.Message, Does.Contain("Suggestion:"));
    }

    [Test]
    public void PropertyRedacted_ThrowsMcpException()
    {
        this.fake.OnGetProperties = (_, _, _, _, _, _, _) =>
            throw new SnoopException(SnoopErrorCode.PropertyRedacted, "Property is redacted");

        var ex = Assert.ThrowsAsync<McpException>(
            () => this.tool.GetPropertiesAsync("0:1"));

        Assert.That(ex!.Message, Does.Contain("PROPERTY_REDACTED"));
    }
}
