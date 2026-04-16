namespace SnoopWPF.Agent.Engine.Binding;

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// Resolves the full data-binding chain for a dependency property on a WPF element (M2-08).
/// Given a target <see cref="DependencyObject"/> and a property name, walks:
///   - Binding Path + intermediate values at each path step.
///   - Converter type name and parameter.
///   - Binding Mode.
///   - Validation errors via <c>Validation.GetErrors</c>.
///   - Status: OK, PathError, ValidationError, MissingDataContext, ConverterError, NoBinding.
///
/// IMPORTANT: <see cref="Resolve"/> MUST be called from the WPF Dispatcher thread.
/// </summary>
internal sealed class BindingResolver
{
    /// <summary>
    /// Resolves the binding chain for <paramref name="propertyName"/> on <paramref name="target"/>.
    /// Must be called on the WPF Dispatcher thread.
    /// </summary>
    /// <param name="target">The WPF element to inspect.</param>
    /// <param name="propertyName">The dependency property name (case-insensitive).</param>
    /// <returns>A fully populated <see cref="BindingResolutionDto"/>.</returns>
    public BindingResolutionDto Resolve(object target, string propertyName)
    {
        if (target is null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (string.IsNullOrEmpty(propertyName))
        {
            throw new ArgumentException("Property name must not be null or empty.", nameof(propertyName));
        }

        // Find the DependencyProperty by name.
        if (target is not DependencyObject depObj)
        {
            return NoBinding();
        }

        var dp = FindDependencyProperty(depObj, propertyName);
        if (dp is null)
        {
            return NoBinding();
        }

        // Retrieve the binding and expression.
        var bindingBase = BindingOperations.GetBindingBase(depObj, dp);
        if (bindingBase is null)
        {
            return NoBinding();
        }

        var expressionBase = BindingOperations.GetBindingExpressionBase(depObj, dp);

        // Handle Binding (simple) vs MultiBinding vs PriorityBinding.
        if (bindingBase is Binding simpleBinding)
        {
            return this.ResolveSimpleBinding(depObj, dp, simpleBinding, expressionBase);
        }

        // MultiBinding / PriorityBinding: return partial info (path = "").
        return ResolveGenericBinding(depObj, dp, bindingBase, expressionBase);
    }

    // -------------------------------------------------------------------------
    // Private — per-binding-type resolution
    // -------------------------------------------------------------------------

    private BindingResolutionDto ResolveSimpleBinding(
        DependencyObject depObj,
        DependencyProperty dp,
        Binding binding,
        BindingExpressionBase? expressionBase)
    {
        var dto = new BindingResolutionDto
        {
            HasBinding = true,
            Path = binding.Path?.Path ?? string.Empty,
            Mode = binding.Mode.ToString(),
            ConverterTypeName = binding.Converter?.GetType().Name ?? string.Empty,
            ConverterParameter = binding.ConverterParameter?.ToString(),
        };

        // Resolve the source object.
        object? source = ResolveBindingSource(depObj, binding, expressionBase);

        if (source is null)
        {
            // No source — check for missing DataContext.
            bool reliesOnDataContext =
                binding.Source is null &&
                binding.ElementName is null &&
                binding.RelativeSource is null;

            if (reliesOnDataContext)
            {
                dto.Status = BindingResolutionStatus.MissingDataContext;
                dto.ErrorDetail = "DataContext is null and no explicit source is configured.";
                return dto;
            }

            dto.SourceTypeName = string.Empty;
            dto.SourceValue = null;
        }
        else
        {
            dto.SourceTypeName = source.GetType().Name;
            dto.SourceValue = SafeToString(source);
        }

        // Walk path steps.
        var pathString = binding.Path?.Path ?? string.Empty;
        bool pathError = false;

        if (!string.IsNullOrEmpty(pathString) && source is not null)
        {
            WalkPathSteps(source, pathString, dto.PathSteps, out pathError);
        }

        // Collect validation errors.
        CollectValidationErrors(depObj, dp, dto.ValidationErrors);

        // Determine status.
        if (pathError)
        {
            dto.Status = BindingResolutionStatus.PathError;
            var failedStep = dto.PathSteps.FindLast(s => s.IsError);
            dto.ErrorDetail = failedStep?.ErrorDetail;
        }
        else if (dto.ValidationErrors.Count > 0)
        {
            dto.Status = BindingResolutionStatus.ValidationError;
            dto.ErrorDetail = string.Join("; ", dto.ValidationErrors);
        }
        else if (expressionBase is not null && expressionBase.HasError)
        {
            // BindingExpression has an error but path walked fine — could be converter.
            dto.Status = BindingResolutionStatus.ConverterError;
            dto.ErrorDetail = GetExpressionError(expressionBase);
        }
        else
        {
            dto.Status = BindingResolutionStatus.OK;
        }

        return dto;
    }

    private static BindingResolutionDto ResolveGenericBinding(
        DependencyObject depObj,
        DependencyProperty dp,
        BindingBase bindingBase,
        BindingExpressionBase? expressionBase)
    {
        var dto = new BindingResolutionDto
        {
            HasBinding = true,
            Path = string.Empty,
            Mode = string.Empty,
        };

        if (bindingBase is MultiBinding mb)
        {
            dto.Mode = mb.Mode.ToString();
        }

        CollectValidationErrors(depObj, dp, dto.ValidationErrors);

        if (dto.ValidationErrors.Count > 0)
        {
            dto.Status = BindingResolutionStatus.ValidationError;
        }
        else if (expressionBase is not null && expressionBase.HasError)
        {
            dto.Status = BindingResolutionStatus.ConverterError;
            dto.ErrorDetail = GetExpressionError(expressionBase);
        }
        else
        {
            dto.Status = BindingResolutionStatus.OK;
        }

        return dto;
    }

    // -------------------------------------------------------------------------
    // Private — source resolution
    // -------------------------------------------------------------------------

    private static object? ResolveBindingSource(
        DependencyObject depObj,
        Binding binding,
        BindingExpressionBase? expressionBase)
    {
        // Explicit source takes precedence.
        if (binding.Source is not null)
        {
            return binding.Source;
        }

        // ElementName: find the named element in the same name scope.
        if (!string.IsNullOrEmpty(binding.ElementName))
        {
            if (depObj is FrameworkElement fe)
            {
                return fe.FindName(binding.ElementName);
            }

            return null;
        }

        // RelativeSource: attempt to resolve from the expression.
        if (binding.RelativeSource is not null)
        {
            if (expressionBase is BindingExpression expr)
            {
                try
                {
                    return expr.ResolvedSource;
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        // Default: DataContext.
        if (depObj is FrameworkElement feForDc)
        {
            return feForDc.DataContext;
        }

        if (depObj is FrameworkContentElement fce)
        {
            return fce.DataContext;
        }

        return null;
    }

    // -------------------------------------------------------------------------
    // Private — path walking
    // -------------------------------------------------------------------------

    private static void WalkPathSteps(
        object source,
        string pathString,
        List<PathStepDto> steps,
        out bool hasError)
    {
        hasError = false;

        // Split on '.' respecting indexers (e.g. "[0]" is kept as a token).
        var segments = SplitPath(pathString);

        object? current = source;

        foreach (var segment in segments)
        {
            if (current is null)
            {
                steps.Add(new PathStepDto
                {
                    Segment = segment,
                    IsError = true,
                    ErrorDetail = "Value is null; cannot traverse further.",
                });
                hasError = true;
                return;
            }

            object? next;
            string? errorDetail;

            if (TryGetPropertyValue(current, segment, out next, out errorDetail))
            {
                steps.Add(new PathStepDto
                {
                    Segment = segment,
                    TypeName = next?.GetType().Name ?? string.Empty,
                    Value = next is null ? null : SafeToString(next),
                    IsError = false,
                });
                current = next;
            }
            else
            {
                steps.Add(new PathStepDto
                {
                    Segment = segment,
                    IsError = true,
                    ErrorDetail = errorDetail ?? $"Property '{segment}' not found on type '{current.GetType().Name}'.",
                });
                hasError = true;
                return;
            }
        }
    }

    private static bool TryGetPropertyValue(
        object obj,
        string segment,
        out object? value,
        out string? errorDetail)
    {
        value = null;
        errorDetail = null;

        // Indexer: [0] or [key]
        if (segment.StartsWith("[", StringComparison.Ordinal) &&
            segment.EndsWith("]", StringComparison.Ordinal))
        {
            var index = segment.Substring(1, segment.Length - 2);
            return TryGetIndexerValue(obj, index, out value, out errorDetail);
        }

        var type = obj.GetType();

        // Prefer public instance property.
        var propInfo = type.GetProperty(
            segment,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

        if (propInfo is not null)
        {
            try
            {
                value = propInfo.GetValue(obj);
                return true;
            }
            catch (Exception ex)
            {
                errorDetail = $"Exception reading '{segment}': {ex.Message}";
                return false;
            }
        }

        errorDetail = $"Property '{segment}' not found on type '{type.Name}'.";
        return false;
    }

    private static bool TryGetIndexerValue(
        object obj,
        string indexStr,
        out object? value,
        out string? errorDetail)
    {
        value = null;
        errorDetail = null;

        // Integer indexer.
        if (int.TryParse(indexStr, out var intIndex))
        {
            var type = obj.GetType();
            var indexerProp = type.GetProperty("Item",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                returnType: null,
                types: new[] { typeof(int) },
                modifiers: null);

            if (indexerProp is not null)
            {
                try
                {
                    value = indexerProp.GetValue(obj, new object[] { intIndex });
                    return true;
                }
                catch (Exception ex)
                {
                    errorDetail = $"Exception at index [{intIndex}]: {ex.Message}";
                    return false;
                }
            }
        }

        // String indexer.
        {
            var type = obj.GetType();
            var indexerProp = type.GetProperty("Item",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                returnType: null,
                types: new[] { typeof(string) },
                modifiers: null);

            if (indexerProp is not null)
            {
                try
                {
                    value = indexerProp.GetValue(obj, new object[] { indexStr });
                    return true;
                }
                catch (Exception ex)
                {
                    errorDetail = $"Exception at index [{indexStr}]: {ex.Message}";
                    return false;
                }
            }
        }

        errorDetail = $"No indexer found on type '{obj.GetType().Name}' for [{indexStr}].";
        return false;
    }

    /// <summary>
    /// Splits a WPF binding path string on '.' boundaries, preserving indexer brackets.
    /// E.g. "Items[0].Name" -> ["Items[0]", "Name"].
    /// </summary>
    private static List<string> SplitPath(string path)
    {
        var segments = new List<string>();
        var current = new System.Text.StringBuilder();

        for (var i = 0; i < path.Length; i++)
        {
            var c = path[i];

            if (c == '[')
            {
                // Flush pending segment if any.
                if (current.Length > 0)
                {
                    segments.Add(current.ToString());
                    current.Clear();
                }

                // Collect until ']'.
                var sb2 = new System.Text.StringBuilder();
                sb2.Append(c);
                i++;
                while (i < path.Length && path[i] != ']')
                {
                    sb2.Append(path[i]);
                    i++;
                }

                if (i < path.Length)
                {
                    sb2.Append(path[i]); // append ']'
                }

                segments.Add(sb2.ToString());
            }
            else if (c == '.')
            {
                if (current.Length > 0)
                {
                    segments.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            segments.Add(current.ToString());
        }

        return segments;
    }

    // -------------------------------------------------------------------------
    // Private — validation errors
    // -------------------------------------------------------------------------

    private static void CollectValidationErrors(
        DependencyObject depObj,
        DependencyProperty dp,
        List<string> errors)
    {
        // Element-level validation errors.
        var elementErrors = Validation.GetErrors(depObj);
        foreach (var err in elementErrors)
        {
            errors.Add(err.ErrorContent?.ToString() ?? "Validation error");
        }
    }

    // -------------------------------------------------------------------------
    // Private — helpers
    // -------------------------------------------------------------------------

    private static DependencyProperty? FindDependencyProperty(DependencyObject depObj, string name)
    {
        var type = depObj.GetType();

        // Walk the type hierarchy to find the DependencyProperty field.
        var current = type;
        while (current is not null && current != typeof(object))
        {
            var fieldName = name + "Property";
            var field = current.GetField(
                fieldName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

            if (field?.GetValue(null) is DependencyProperty dp)
            {
                return dp;
            }

            current = current.BaseType;
        }

        return null;
    }

    private static string? GetExpressionError(BindingExpressionBase expr)
    {
        try
        {
            if (expr is BindingExpression simple)
            {
                return simple.ParentBinding?.Path?.Path is { } p && !string.IsNullOrEmpty(p)
                    ? $"Binding error on path '{p}'"
                    : "Binding expression has an error.";
            }

            return "Binding expression has an error.";
        }
        catch
        {
            return "Binding expression has an error.";
        }
    }

    private static string SafeToString(object obj)
    {
        try
        {
            return obj.ToString() ?? string.Empty;
        }
        catch
        {
            return $"<{obj.GetType().Name}>";
        }
    }

    private static BindingResolutionDto NoBinding() =>
        new() { HasBinding = false, Status = BindingResolutionStatus.NoBinding };
}
