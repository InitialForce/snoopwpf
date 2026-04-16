namespace SnoopWPF.Agent.Tests;

using System.Collections.Generic;
using System.Threading;
using System.Windows;
using NUnit.Framework;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Input.Deterministic;

/// <summary>
/// Unit tests for <see cref="InputStrategySelector"/> — verifying all gate/tier
/// enforcement paths required by M1-15 acceptance criteria.
/// </summary>
[TestFixture]
[Apartment(System.Threading.ApartmentState.STA)]
public class InputStrategySelectorTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static InputIntent MakeIntent(InputIntentKind kind) =>
        new() { Kind = kind };

    private static DependencyObject MakeTarget() => new DependencyObject();

    private static IReadOnlyList<IDeterministicInputStrategy> OneStrategy(InputTier tier) =>
        new[] { new FakeStrategy(tier) };

    // ---------------------------------------------------------------------------
    // S7: Injection mode rejects every strategy
    // ---------------------------------------------------------------------------

    [Test]
    public void Select_InjectionMode_RejectsAllStrategies()
    {
        var policy = SessionPolicy.Create(SessionMode.Injection, new SnoopAgentOptions
        {
            MaxTier = InputTier.L1,
            EnableAutomation = true,
            EnableMutation = true,
        });

        var selector = new InputStrategySelector(policy, OneStrategy(InputTier.L0));
        var intents = new[]
        {
            MakeIntent(InputIntentKind.Click),
            MakeIntent(InputIntentKind.Toggle),
            MakeIntent(InputIntentKind.ExpandCollapse),
            MakeIntent(InputIntentKind.SetCheckState),
            MakeIntent(InputIntentKind.SetTextValue),
            MakeIntent(InputIntentKind.SelectItem),
            MakeIntent(InputIntentKind.ExecuteCommand),
            MakeIntent(InputIntentKind.SetProperty),
        };

        var target = MakeTarget();
        foreach (var intent in intents)
        {
            var result = selector.Select(intent, target, out var reason);
            Assert.That(result, Is.Null, $"Injection must reject {intent.Kind}");
            Assert.That(reason, Is.Not.Null, $"Must return a FailureReason for {intent.Kind}");
        }
    }

    // ---------------------------------------------------------------------------
    // Mutation gate
    // ---------------------------------------------------------------------------

    [Test]
    public void Select_MutationDisabled_RejectsSetProperty()
    {
        var policy = SessionPolicy.Create(SessionMode.CoLocated, new SnoopAgentOptions
        {
            MaxTier = InputTier.L1,
            EnableAutomation = true,
            EnableMutation = false, // gate under test
        });

        var selector = new InputStrategySelector(policy, OneStrategy(InputTier.L1));
        var result = selector.Select(MakeIntent(InputIntentKind.SetProperty), MakeTarget(), out var reason);

        Assert.That(result, Is.Null);
        Assert.That(reason, Is.EqualTo(FailureReason.MutationDisabled));
    }

    [Test]
    public void Select_MutationDisabled_AllowsNonMutationIntents()
    {
        var policy = SessionPolicy.Create(SessionMode.CoLocated, new SnoopAgentOptions
        {
            MaxTier = InputTier.L1,
            EnableAutomation = true,
            EnableMutation = false,
        });

        // ExecuteCommand is not a mutation intent — should succeed.
        var selector = new InputStrategySelector(policy, OneStrategy(InputTier.L0));
        var result = selector.Select(MakeIntent(InputIntentKind.ExecuteCommand), MakeTarget(), out var reason);

        Assert.That(result, Is.Not.Null, "ExecuteCommand must not be blocked by MutationDisabled");
        Assert.That(reason, Is.Null);
    }

    // ---------------------------------------------------------------------------
    // Automation gate
    // ---------------------------------------------------------------------------

    [Test]
    [TestCase(InputIntentKind.Click)]
    [TestCase(InputIntentKind.Toggle)]
    [TestCase(InputIntentKind.ExpandCollapse)]
    public void Select_AutomationDisabled_RejectsAutomationIntents(InputIntentKind kind)
    {
        var policy = SessionPolicy.Create(SessionMode.CoLocated, new SnoopAgentOptions
        {
            MaxTier = InputTier.L1,
            EnableAutomation = false, // gate under test
            EnableMutation = true,
        });

        var selector = new InputStrategySelector(policy, OneStrategy(InputTier.L1));
        var result = selector.Select(MakeIntent(kind), MakeTarget(), out var reason);

        Assert.That(result, Is.Null);
        Assert.That(reason, Is.EqualTo(FailureReason.AutomationDisabled));
    }

    // ---------------------------------------------------------------------------
    // MaxTier gate
    // ---------------------------------------------------------------------------

    [Test]
    public void Select_MaxTierL0_RejectsL1Strategy()
    {
        var policy = SessionPolicy.Create(SessionMode.CoLocated, new SnoopAgentOptions
        {
            MaxTier = InputTier.L0, // gate under test
            EnableAutomation = true,
            EnableMutation = true,
        });

        var selector = new InputStrategySelector(policy, OneStrategy(InputTier.L1));
        var result = selector.Select(MakeIntent(InputIntentKind.Click), MakeTarget(), out var reason);

        Assert.That(result, Is.Null);
        Assert.That(reason, Is.EqualTo(FailureReason.TierMismatch));
    }

    [Test]
    public void Select_MaxTierL1_AllowsL0AndL1Strategies()
    {
        var policy = SessionPolicy.Create(SessionMode.CoLocated, new SnoopAgentOptions
        {
            MaxTier = InputTier.L1,
            EnableAutomation = true,
            EnableMutation = true,
        });

        // L0 strategy.
        var selectorL0 = new InputStrategySelector(policy, OneStrategy(InputTier.L0));
        var resultL0 = selectorL0.Select(MakeIntent(InputIntentKind.ExecuteCommand), MakeTarget(), out _);
        Assert.That(resultL0, Is.Not.Null, "L0 strategy should be accepted when MaxTier=L1");

        // L1 strategy.
        var selectorL1 = new InputStrategySelector(policy, OneStrategy(InputTier.L1));
        var resultL1 = selectorL1.Select(MakeIntent(InputIntentKind.Click), MakeTarget(), out _);
        Assert.That(resultL1, Is.Not.Null, "L1 strategy should be accepted when MaxTier=L1");
    }

    // ---------------------------------------------------------------------------
    // Happy path
    // ---------------------------------------------------------------------------

    [Test]
    public void Select_AllGatesPass_ReturnsStrategy()
    {
        var policy = SessionPolicy.Create(SessionMode.CoLocated, new SnoopAgentOptions
        {
            MaxTier = InputTier.L1,
            EnableAutomation = true,
            EnableMutation = true,
        });

        var strategy = new FakeStrategy(InputTier.L1);
        var selector = new InputStrategySelector(policy, new[] { strategy });
        var result = selector.Select(MakeIntent(InputIntentKind.Click), MakeTarget(), out var reason);

        Assert.That(result, Is.SameAs(strategy));
        Assert.That(reason, Is.Null);
    }

    [Test]
    public void Select_NoStrategyCanHandle_ReturnsTierMismatch()
    {
        var policy = SessionPolicy.Create(SessionMode.CoLocated, new SnoopAgentOptions
        {
            MaxTier = InputTier.L1,
            EnableAutomation = true,
            EnableMutation = true,
        });

        var strategy = new FakeStrategy(InputTier.L1, canHandle: false);
        var selector = new InputStrategySelector(policy, new[] { strategy });
        var result = selector.Select(MakeIntent(InputIntentKind.Click), MakeTarget(), out var reason);

        Assert.That(result, Is.Null);
        Assert.That(reason, Is.EqualTo(FailureReason.TierMismatch));
    }

    // ---------------------------------------------------------------------------
    // Fake strategy (nested private class — StyleCop file layout: after tests)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// A configurable fake strategy for testing. Accepts any intent/target by default.
    /// </summary>
    private sealed class FakeStrategy : IDeterministicInputStrategy
    {
        private readonly bool canHandle;

        public FakeStrategy(InputTier tier, bool canHandle = true)
        {
            this.Tier = tier;
            this.canHandle = canHandle;
        }

        public InputTier Tier { get; }

        public bool CanHandle(InputIntent intent, DependencyObject target) => this.canHandle;

        public DeterministicInputResult Invoke(DependencyObject target, InputIntent intent, CancellationToken ct) =>
            new() { Success = true, ChosenTier = this.Tier };
    }
}
