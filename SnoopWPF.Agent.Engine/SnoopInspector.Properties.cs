// SnoopInspector.Properties.cs
// FX6-A7: Property inspection and mutation.

namespace SnoopWPF.Agent.Engine;

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Snoop.Data.Tree;
using Snoop.Infrastructure;
using Snoop.Infrastructure.Diagnostics;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;
using SnoopWPF.Agent.Engine.Infrastructure;
using SnoopWPF.Agent.Engine.StateDelta;
using SnoopWPF.Agent.Engine.Sync;

/// <content/>
public sealed partial class SnoopInspector
{
    // FX6-A5: attribute filter constant for PropertyInformation.GetAllProperties.
    private static readonly Attribute[] AllPropertiesAttributeFilter = { new PropertyFilterAttribute(PropertyFilterOptions.All) };

    /// <inheritdoc/>
    public Task<CursorPage<PropertyDto>> GetPropertiesAsync(
        string nodeId,
        string? filter,
        string? category,
        bool includeDefaults,
        string? cursor,
        int take,
        CancellationToken ct)
    {
        return this.RunOnDispatcherAsync(() =>
        {
            int effectiveTake = Math.Min(Math.Max(take, 1), InputConstants.MaxPageSize);
            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);

            // FX6-A5: Allocation-storm fix.
            //
            // Previous approach: PropertyInformation.GetProperties() creates one PropertyInformation
            // DependencyObject (with DP bindings) per property — ~300 allocations for a typical Window
            // — then iterates ALL before paging. This stalls the Dispatcher for 50-100 ms per call.
            //
            // New approach (descriptor-first):
            //   1. Enumerate PropertyDescriptors WITHOUT creating PropertyInformation objects.
            //   2. Apply includeDefaults/category/text filters using cheap DP reads.
            //   3. Build sorted name snapshot → cursor → page indices.
            //   4. Create PropertyInformation ONLY for the page window (≤ effectiveTake items).
            //
            // Extended-properties edge case (ResourceDictionary, AutomationPeer, etc.) falls back to
            // the old full-scan path because those objects don't benefit from descriptor-first filtering
            // and their property counts are small.
            var dependencyObjectTarget = target as DependencyObject;

            // Use descriptor-first fast path for DependencyObject targets only.
            if (dependencyObjectTarget is not null
                && !(target is ResourceDictionary)
                && !(target is ICollection))
            {
                return this.GetPropertiesFastPath(
                    target, dependencyObjectTarget, nodeId,
                    filter, category, includeDefaults,
                    cursor, effectiveTake);
            }

            // ── Legacy full-scan path for non-DO / collection / ResourceDictionary targets ────────

            List<PropertyDto> allDtos;

            var props = PropertyInformation.GetProperties(target);
            try
            {
                allDtos = new List<PropertyDto>(props.Count);

                foreach (var prop in props)
                {
                    if (!includeDefaults)
                    {
                        if (!prop.IsLocallySet && !prop.IsDatabound && !prop.IsInvalidBinding && !prop.IsExpression)
                        {
                            continue;
                        }
                    }

                    if (!string.IsNullOrEmpty(category) && !string.Equals(category, "all", StringComparison.OrdinalIgnoreCase))
                    {
                        var propCategory = prop.Property?.Category ?? string.Empty;
                        if (!string.Equals(propCategory, category, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    var dto = DtoProjection.ToPropertyDto(prop, this.options.EnableRedaction);
                    allDtos.Add(dto);
                }
            }
            finally
            {
                foreach (var prop in props)
                {
                    prop.Teardown();
                    StopChangeTimer(prop);
                }
            }

            if (!string.IsNullOrEmpty(filter))
            {
                allDtos = allDtos
                    .Where(p => p.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            var legacyNodeIds = allDtos.Select((_, i) => i.ToString()).ToList();
            var legacyCursorToken = !string.IsNullOrEmpty(cursor)
                ? cursor
                : this.cursorManager.CreateCursor(legacyNodeIds, nodeId: nodeId);
            var legacyPage = this.cursorManager.GetPage(legacyCursorToken, effectiveTake, nodeId: nodeId);

            var legacyPageItems = new List<PropertyDto>(legacyPage.Items.Count);
            foreach (var idxStr in legacyPage.Items)
            {
                if (int.TryParse(idxStr, out var idx) && idx < allDtos.Count)
                {
                    legacyPageItems.Add(allDtos[idx]);
                }
            }

            return new CursorPage<PropertyDto>
            {
                Items = legacyPageItems,
                NextCursor = legacyPage.NextCursor,
                TotalCount = legacyPage.TotalCount,
                HasMore = legacyPage.HasMore,
                Stale = legacyPage.Stale,
            };
        }, ct);
    }

    /// <summary>
    /// FX6-A5: Descriptor-first fast path for <see cref="GetPropertiesAsync"/> on DependencyObject targets.
    /// Avoids the allocation storm of creating a <see cref="PropertyInformation"/> per property before paging.
    /// </summary>
    private CursorPage<PropertyDto> GetPropertiesFastPath(
        object target,
        DependencyObject d,
        string nodeId,
        string? filter,
        string? category,
        bool includeDefaults,
        string? cursor,
        int effectiveTake)
    {
        // Step 1: Get descriptors without constructing PropertyInformation objects.
        var allDescriptors = PropertyInformation.GetAllProperties(target, AllPropertiesAttributeFilter);

        // Step 2: Apply pertinence + includeDefaults + category filters cheaply.
        var filtered = new List<PropertyDescriptor>(allDescriptors.Count);
        foreach (var desc in allDescriptors)
        {
            // Apply the same pertinence filter as PropertyInformation.GetProperties.
            if (!PertinentPropertyFilter.Filter(target, desc))
            {
                continue;
            }

            // includeDefaults=false: skip properties at their default value.
            if (!includeDefaults)
            {
                var dpDesc = DependencyPropertyDescriptor.FromProperty(desc);
                if (dpDesc?.DependencyProperty is { } dp)
                {
                    // A property is "non-default" if it is locally set, data-bound, has a
                    // binding error, or has an expression (animation/template binding).
                    var localValue = d.ReadLocalValue(dp);
                    bool isLocallySet = localValue != DependencyProperty.UnsetValue;
                    bool isDatabound = isLocallySet && localValue is BindingExpressionBase;
                    bool isExpression = !isDatabound
                        && isLocallySet
                        && DependencyPropertyHelper.GetValueSource(d, dp).IsExpression;

                    if (!isLocallySet && !isDatabound && !isExpression)
                    {
                        continue;
                    }
                }

                // Non-DP properties (plain CLR props) are always included.
            }

            if (!string.IsNullOrEmpty(category)
                && !string.Equals(category, "all", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(desc.Category, category, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            filtered.Add(desc);
        }

        // Step 3: Sort by DisplayName (consistent with PropertyInformation.CompareTo).
        filtered.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal));

        // Step 4: Apply text filter on display name.
        if (!string.IsNullOrEmpty(filter))
        {
            filtered = filtered
                .Where(d2 => d2.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

        // Also include DefaultStyleKey for FrameworkElement/FrameworkContentElement if applicable.
        // PropertyInformation.GetProperties adds this after the sorted list.
        // We add it here if it would not have been skipped by the filters above.
        if (target is FrameworkElement or FrameworkContentElement)
        {
            const string defaultStyleKeyName = "DefaultStyleKey";
            var clrProp = target.GetType().GetProperty(
                defaultStyleKeyName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

            if (clrProp is not null && !filtered.Exists(p => p.Name == defaultStyleKeyName))
            {
                // Only include if it passes the text filter.
                if (string.IsNullOrEmpty(filter)
                    || defaultStyleKeyName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var dskDesc = TypeDescriptor.CreateProperty(target.GetType(), defaultStyleKeyName, typeof(Style));
                    filtered.Add(dskDesc);
                }
            }
        }

        // Step 5: Build cursor snapshot and page.
        var nameSnapshot = filtered.Select((_, i) => i.ToString()).ToList();
        var cursorToken = !string.IsNullOrEmpty(cursor)
            ? cursor
            : this.cursorManager.CreateCursor(nameSnapshot, nodeId: nodeId);
        var page = this.cursorManager.GetPage(cursorToken, effectiveTake, nodeId: nodeId);

        // Step 6: Create PropertyInformation ONLY for the page window.
        var pageItems = new List<PropertyDto>(page.Items.Count);
        foreach (var idxStr in page.Items)
        {
            if (!int.TryParse(idxStr, out var idx) || idx >= filtered.Count)
            {
                continue;
            }

            var desc = filtered[idx];
            var prop = new PropertyInformation(target, desc, desc.Name, desc.DisplayName);
            try
            {
                pageItems.Add(DtoProjection.ToPropertyDto(prop, this.options.EnableRedaction));
            }
            finally
            {
                prop.Teardown();
                StopChangeTimer(prop);
            }
        }

        return new CursorPage<PropertyDto>
        {
            Items = pageItems,
            NextCursor = page.NextCursor,
            TotalCount = page.TotalCount,
            HasMore = page.HasMore,
            Stale = page.Stale,
        };
    }


    public Task<StateDeltaDto> SetPropertyAsync(string nodeId, string propertyName, string value, CancellationToken ct)
    {
        // FX2-C9 (EC-C2): validate at the public boundary before dispatching to the
        // Dispatcher lambda. A null nodeId would trigger NullReferenceException inside
        // ResolveNodeOrThrow and surface as an opaque InternalError; null propertyName
        // flows into the redaction filter the same way. Contrast: SetCheckStateAsync,
        // SelectItemAsync, SetSliderValueAsync all guard here.
        if (string.IsNullOrEmpty(nodeId))
        {
            throw new ArgumentException("nodeId must not be null or empty.", nameof(nodeId));
        }

        if (string.IsNullOrEmpty(propertyName))
        {
            throw new ArgumentException("propertyName must not be null or empty.", nameof(propertyName));
        }

        return this.RunOnDispatcherAsync(() =>
        {
            // FX-M8: enforce MaxTier before any mutation.
            var tierResult = this.EnsureMutationTier();
            if (tierResult is not null)
            {
                return tierResult;
            }

            // Guard: mutation must be explicitly enabled.
            if (!this.options.EnableMutation)
            {
                throw new SnoopException(
                    SnoopErrorCode.MutationDisabled,
                    "Mutation is disabled. Set EnableMutation=true in SnoopInspectorOptions.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.MutationDisabled });
            }

            var target = this.ResolveNodeOrThrow(nodeId);
            this.VerifyElementConnectivity(target, nodeId);

            // Guard: redacted properties cannot be mutated.
            var isRedacted = this.options.EnableRedaction && RedactionFilter.IsRedacted(propertyName, null);
            if (isRedacted)
            {
                throw new SnoopException(
                    SnoopErrorCode.PropertyRedacted,
                    $"Property '{propertyName}' is redacted and cannot be set.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.PropertyRedacted });
            }

            // Only DependencyObject targets support DP-based property setting.
            if (target is not DependencyObject depObj)
            {
                throw new SnoopException(
                    SnoopErrorCode.UnsupportedPropertyType,
                    $"Target '{nodeId}' is not a DependencyObject; property mutation requires a DependencyObject.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.UnsupportedPropertyType });
            }

            // Find the DependencyProperty via PropertyInformation.
            // ONE synchronous block: get properties, find named one, capture type, teardown.
            DependencyProperty? depProp = null;
            Type? propertyType = null;
            string previousValueRaw = string.Empty;

            var props = PropertyInformation.GetProperties(target);
            try
            {
                var match = props.FirstOrDefault(p =>
                    string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.DisplayName, propertyName, StringComparison.OrdinalIgnoreCase));

                if (match is null)
                {
                    throw new SnoopException(
                        SnoopErrorCode.PropertyReadOnly,
                        $"Property '{propertyName}' was not found on '{target.GetType().Name}'.",
                        targetId: nodeId,
                        suggestions: new[] { SnoopSuggestions.PropertyReadOnly });
                }

                if (!match.CanEdit)
                {
                    throw new SnoopException(
                        SnoopErrorCode.PropertyReadOnly,
                        $"Property '{propertyName}' is read-only on '{target.GetType().Name}'.",
                        targetId: nodeId,
                        suggestions: new[] { SnoopSuggestions.PropertyReadOnly });
                }

                depProp = match.DependencyProperty;
                propertyType = (Type?)match.PropertyType;

                // Capture previous value (use StringValue — never TypeDescriptor).
                previousValueRaw = match.StringValue ?? string.Empty;
            }
            finally
            {
                foreach (var prop in props)
                {
                    prop.Teardown();
                    StopChangeTimer(prop);
                }
            }

            if (depProp is null || propertyType is null)
            {
                throw new SnoopException(
                    SnoopErrorCode.UnsupportedPropertyType,
                    $"Property '{propertyName}' on '{target.GetType().Name}' is not a DependencyProperty and cannot be set.",
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.UnsupportedPropertyType });
            }

            // Convert the value using the hardcoded TypeConverterTable — NEVER TypeDescriptor.GetConverter().
            object convertedValue;
            try
            {
                convertedValue = TypeConverterTable.Convert(propertyType, value);
            }
            catch (NotSupportedException ex)
            {
                throw new SnoopException(
                    SnoopErrorCode.UnsupportedPropertyType,
                    ex.Message,
                    ex,
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.UnsupportedPropertyType });
            }
            catch (FormatException ex)
            {
                throw new SnoopException(
                    SnoopErrorCode.TypeConversionFailed,
                    ex.Message,
                    ex,
                    targetId: nodeId,
                    suggestions: new[] { SnoopSuggestions.TypeConversionFailed });
            }

            // Apply the value.
            // FX2-C8: use SetCurrentValue, not SetValue. SetValue writes at Local priority
            // which permanently shadows any TwoWay binding on depProp. SetCurrentValue
            // updates at the current effective priority, cooperating with binding
            // expressions (matching Snoop Classic behaviour).
            depObj.SetCurrentValue(depProp, convertedValue);

            // Log mutation: nodeId + property name only (NOT values — per security rules).
            System.Diagnostics.Trace.WriteLine(
                $"[SnoopWPF.Agent] SetProperty: nodeId={nodeId}, property={propertyName}");

            // M1-11: compute stateChanged at serialization time by reading the observable DP
            // value NOW, after any re-entrant PropertyChangedCallback has settled.  This closes
            // the false-positive where a reverting callback resets the value but the old code
            // compared previousValue to the *requested* convertedValue string (PRD §7.3 W3-C1).
            var stateChanged = StateDeltaSerializationHook.ComputeStateChanged(depObj, depProp, previousValueRaw);

            // newValue reflects the actual settled observable state (not the requested string).
            // ADV-PI: GetValue result + StringValue are ViewModel data — guard injection boundary at DTO emit.
            var previousValue = PromptInjectionGuard.Quote(previousValueRaw);
            var newValue = PromptInjectionGuard.Quote(depObj.GetValue(depProp)?.ToString() ?? string.Empty);

            if (!stateChanged)
            {
                return StateDeltaSerializationHook.CreateUnchanged(locator: null) with
                {
                    ElementVisible = true,
                    PreviousValue = previousValue,
                    NewValue = newValue,
                };
            }

            return new StateDeltaDto
            {
                Success = true,
                ElementVisible = true,
                StateChanged = true,
                PreviousValue = previousValue,
                NewValue = newValue,
            };
        }, ct);
    }

    /// <inheritdoc/>

    public async Task<CursorPage<PropertyDto>> GetPropertiesAsync(
        WpfLocator locator,
        string? filter,
        string? category,
        bool includeDefaults,
        string? cursor,
        int take,
        CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.GetPropertiesAsync(nodeId, filter, category, includeDefaults, cursor, take, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<StateDeltaDto> SetPropertyAsync(
        WpfLocator locator,
        string propertyName,
        string value,
        CancellationToken ct)
    {
        var nodeId = await this.ResolveLocatorAsync(locator, ct).ConfigureAwait(false);
        return await this.SetPropertyAsync(nodeId, propertyName, value, ct).ConfigureAwait(false);
    }
}
