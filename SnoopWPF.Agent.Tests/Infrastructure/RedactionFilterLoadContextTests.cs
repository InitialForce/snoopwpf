namespace SnoopWPF.Agent.Tests.Infrastructure;

using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using NUnit.Framework;
using SnoopWPF.Agent.Engine.Infrastructure;

/// <summary>
/// FX-M9 (bd-26k): SensitiveAttribute comparison must use FullName + assembly name, not
/// reference equality, so that [Sensitive]-decorated types are detected even when the
/// Contracts assembly was loaded in an isolated AssemblyLoadContext (injection mode).
/// </summary>
[TestFixture]
public class RedactionFilterLoadContextTests
{
    /// <summary>
    /// Loads SnoopWPF.Agent.Contracts.dll in a fresh, isolated AssemblyLoadContext,
    /// emits a new type decorated with [Sensitive] from that secondary context,
    /// then verifies that IsStructurallySensitive still returns true.
    /// </summary>
    [Test]
    public void IsStructurallySensitive_SensitiveAttributeFromSecondaryLoadContext_ReturnsTrue()
    {
        // Arrange — load Contracts in a separate context so the attribute type identity differs.
        var contractsPath = typeof(SnoopWPF.Agent.Contracts.SensitiveAttribute).Assembly.Location;

        var isolatedCtx = new AssemblyLoadContext("test-isolated", isCollectible: true);
        try
        {
            var secondaryContracts = isolatedCtx.LoadFromAssemblyPath(contractsPath);

            // The SensitiveAttribute type from the secondary context is a *different* Type object.
            var sensitiveAttrTypeFromSecondary = secondaryContracts.GetType(
                typeof(SnoopWPF.Agent.Contracts.SensitiveAttribute).FullName!)!;

            // Sanity check: reference-equality would fail, proving the fix is needed.
            Assert.That(
                sensitiveAttrTypeFromSecondary,
                Is.Not.SameAs(typeof(SnoopWPF.Agent.Contracts.SensitiveAttribute)),
                "The secondary-context type must be a distinct object (pre-fix baseline).");

            // Emit a new class decorated with [Sensitive] from the secondary context.
            var sensitiveAttrCtor = sensitiveAttrTypeFromSecondary
                .GetConstructor(Type.EmptyTypes)!;

            var asmName = new AssemblyName("TestDynamicAssembly_FxM9");
            // RunAndCollect makes this assembly collectible so it can reference a collectible assembly.
            var dynAsm = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.RunAndCollect);
            var dynMod = dynAsm.DefineDynamicModule("MainModule");
            var typeBuilder = dynMod.DefineType(
                "SensitiveStubFromSecondaryCtx",
                TypeAttributes.Public | TypeAttributes.Class);

            // Apply the attribute from the secondary context via CustomAttributeBuilder.
            var attrBuilder = new CustomAttributeBuilder(sensitiveAttrCtor, Array.Empty<object>());
            typeBuilder.SetCustomAttribute(attrBuilder);

            var emittedType = typeBuilder.CreateType()!;
            var instance = Activator.CreateInstance(emittedType)!;

            // Act
            var result = RedactionFilter.IsStructurallySensitive(instance);

            // Assert — FX-M9 fix: FullName+assembly comparison must catch this.
            Assert.That(result, Is.True,
                "IsStructurallySensitive must return true for a type decorated with [Sensitive] " +
                "from a secondary AssemblyLoadContext (FullName+assembly name comparison).");
        }
        finally
        {
            isolatedCtx.Unload();
        }
    }

    /// <summary>
    /// Verifies baseline: same-context [Sensitive] type still works after the fix.
    /// </summary>
    [Test]
    public void IsStructurallySensitive_SensitiveAttributeFromSameContext_StillReturnsTrue()
    {
        var value = new SensitiveStub();

        Assert.That(RedactionFilter.IsStructurallySensitive(value), Is.True,
            "Same-context [Sensitive] type must still be detected after FX-M9 fix.");
    }

    /// <summary>
    /// Control: type without [Sensitive] must not be affected by the fix.
    /// </summary>
    [Test]
    public void IsStructurallySensitive_PlainTypeFromAnyContext_ReturnsFalse()
    {
        Assert.That(RedactionFilter.IsStructurallySensitive(new PlainStub()), Is.False,
            "A type with no [Sensitive] attribute must not be redacted.");
    }

    // ─── Helper types ────────────────────────────────────────────────────────

    [SnoopWPF.Agent.Contracts.Sensitive]
    private sealed class SensitiveStub
    {
        public override string ToString() => "SENSITIVE";
    }

    private sealed class PlainStub
    {
        public override string ToString() => "plain";
    }
}
