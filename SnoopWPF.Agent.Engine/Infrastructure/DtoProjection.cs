namespace SnoopWPF.Agent.Engine.Infrastructure;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Data;
using Snoop.Data.Tree;
using Snoop.Infrastructure;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// Static helpers that convert Snoop.Core engine types to contract DTOs.
/// IMPORTANT: These methods access live WPF objects and MUST only be called
/// from within a Dispatcher.Invoke block. They are NOT thread-safe.
/// Never call TypeDescriptor.GetConverter() here — see global security rules.
/// </summary>
public static class DtoProjection
{
    /// <summary>
    /// Converts a Snoop TreeItem to a NodeDto, assigning a stable ID via the registry.
    /// </summary>
    /// <param name="item">The TreeItem to project.</param>
    /// <param name="registry">Node registry for stable ID assignment.</param>
    /// <param name="depth">Override depth (use item.Depth when negative).</param>
    public static NodeDto ToNodeDto(TreeItem item, NodeRegistry registry, int depth = -1)
    {
        if (item is null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        var nodeId = registry.GetOrCreateId(item.Target);

        return new NodeDto
        {
            NodeId = nodeId,
            TypeName = item.TargetType?.Name ?? string.Empty,
            Name = item.Name ?? string.Empty,
            DisplayName = item.DisplayName ?? string.Empty,
            ChildCount = item.Children.Count,
            HasBindingError = item.HasBindingError,
            Depth = depth >= 0 ? depth : item.Depth,
            ChildrenTruncated = false,
            Properties = null,
            Children = null,
        };
    }

    /// <summary>
    /// Converts a PropertyInformation to a PropertyDto with redaction applied.
    /// Must be called on the Dispatcher thread with propInfo still live (before Teardown).
    /// Never calls TypeDescriptor.GetConverter().
    /// </summary>
    /// <param name="prop">The PropertyInformation to project.</param>
    /// <param name="enableRedaction">Whether to apply redaction filter.</param>
    public static PropertyDto ToPropertyDto(PropertyInformation prop, bool enableRedaction = true)
    {
        if (prop is null)
        {
            throw new ArgumentNullException(nameof(prop));
        }

        var name = prop.DisplayName ?? prop.Name ?? string.Empty;
        var propertyType = (Type?)prop.PropertyType;

        var isRedacted = enableRedaction && RedactionFilter.IsRedacted(name, propertyType);

        // Use StringValue (uses value.ToString()) — NEVER TypeDescriptor.GetConverter()
        var value = isRedacted ? "[REDACTED]" : (prop.StringValue ?? string.Empty);

        var valueSource = prop.ValueSource.BaseValueSource.ToString();
        if (prop.IsExpression)
        {
            valueSource += " (Binding)";
        }

        return new PropertyDto
        {
            Name = name,
            TypeName = propertyType?.Name ?? string.Empty,
            Value = value,
            ValueSource = valueSource,
            IsLocallySet = prop.IsLocallySet,
            IsDataBound = prop.IsDatabound,
            HasBindingError = prop.IsInvalidBinding,
            BindingError = string.IsNullOrEmpty(prop.BindingError) ? null : prop.BindingError,
            IsReadOnly = !prop.CanEdit,
            // HasTypeConverter: DP properties commonly have type converters.
            // We do NOT call TypeDescriptor.GetConverter() per security rules.
            HasTypeConverter = prop.DependencyProperty is not null,
            IsRedacted = isRedacted,
        };
    }

    /// <summary>
    /// Extracts binding information from a PropertyInformation if it has an active binding.
    /// Returns null if the property is not data-bound.
    /// Must be called on the Dispatcher thread.
    /// </summary>
    public static BindingInfoDto? ToBindingInfoDto(PropertyInformation prop)
    {
        if (prop is null)
        {
            throw new ArgumentNullException(nameof(prop));
        }

        if (!prop.IsDatabound && prop.Binding is null)
        {
            return null;
        }

        var binding = prop.Binding;
        if (binding is null)
        {
            return new BindingInfoDto
            {
                HasBinding = false,
            };
        }

        var dto = new BindingInfoDto
        {
            HasBinding = true,
            BindingType = binding.GetType().Name,
        };

        if (binding is Binding simpleBinding)
        {
            dto.Path = simpleBinding.Path?.Path ?? string.Empty;
            dto.ElementName = simpleBinding.ElementName ?? string.Empty;
            dto.Mode = simpleBinding.Mode.ToString();
            dto.UpdateSourceTrigger = simpleBinding.UpdateSourceTrigger.ToString();
            dto.ConverterTypeName = simpleBinding.Converter?.GetType().Name ?? string.Empty;

            if (simpleBinding.RelativeSource is { } rs)
            {
                dto.RelativeSource = rs.Mode.ToString();
            }
        }
        else if (binding is MultiBinding multiBinding)
        {
            dto.Mode = multiBinding.Mode.ToString();
            dto.UpdateSourceTrigger = multiBinding.UpdateSourceTrigger.ToString();

            if (multiBinding.Bindings.Count > 0)
            {
                dto.ChildBindings = new List<BindingInfoDto>();
                foreach (var child in multiBinding.Bindings)
                {
                    if (child is Binding childSimple)
                    {
                        dto.ChildBindings.Add(new BindingInfoDto
                        {
                            HasBinding = true,
                            BindingType = child.GetType().Name,
                            Path = childSimple.Path?.Path ?? string.Empty,
                            ElementName = childSimple.ElementName ?? string.Empty,
                            Mode = childSimple.Mode.ToString(),
                            UpdateSourceTrigger = childSimple.UpdateSourceTrigger.ToString(),
                            ConverterTypeName = childSimple.Converter?.GetType().Name ?? string.Empty,
                        });
                    }
                }
            }
        }

        // Populate binding expression status
        var expression = prop.BindingExpression;
        if (expression is not null)
        {
            dto.Status = expression.Status.ToString();
            dto.Error = expression.HasError ? prop.BindingError : null;
        }

        // Populate data context information from the target FrameworkElement
        if (prop.Target is FrameworkElement fe)
        {
            var dc = fe.DataContext;
            dto.DataContextIsNull = dc is null;
            dto.DataContextType = dc?.GetType().FullName ?? string.Empty;
            dto.ResolvedValue = dc?.ToString();
        }

        return dto;
    }
}
