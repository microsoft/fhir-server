// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.SourceNodeSerialization.SourceNodes.Models;

namespace Microsoft.Health.Fhir.SourceNodeSerialization.SourceNodes;

internal class ReflectedSourceNode : BaseSourceNode<IExtensionData>
{
    private readonly Lazy<List<(string Name, Lazy<IEnumerable<ISourceNode>> Node)>> _propertySourceNodes;

    public ReflectedSourceNode(IExtensionData resource, string location, string name = null)
        : base(resource)
    {
        if (resource is ResourceJsonNode resourceJsonNode)
        {
            Name = name ?? resourceJsonNode.ResourceType;
            ResourceType = resourceJsonNode.ResourceType;
            Location = location ?? resourceJsonNode.ResourceType;
        }
        else
        {
            Name = name;
            Location = location;
        }

        _propertySourceNodes = new Lazy<List<(string Name, Lazy<IEnumerable<ISourceNode>> Node)>>(() =>
        {
            var list = new List<(string Name, Lazy<IEnumerable<ISourceNode>> Node)>();

            foreach (PropertyDescriptor prop in TypeDescriptor.GetProperties(Resource))
            {
                var thisProp = prop;

                // ExtensionData and resourceType are already handled
                if (thisProp.Name == nameof(IExtensionData.ExtensionData) || thisProp.Name == nameof(ResourceJsonNode.ResourceType))
                {
                    continue;
                }

                string propName = (thisProp.Attributes[typeof(JsonPropertyNameAttribute)] as JsonPropertyNameAttribute)?.Name ?? thisProp.Name;

                if (typeof(IExtensionData).IsAssignableFrom(thisProp.PropertyType))
                {
                    list.Add((propName, new Lazy<IEnumerable<ISourceNode>>(() => [new ReflectedSourceNode((IExtensionData)thisProp.GetValue(Resource), $"{Location}.{propName}", propName)])));
                }
                else if (typeof(ICollection).IsAssignableFrom(thisProp.PropertyType))
                {
                    var value = (ICollection)thisProp.GetValue(Resource);
                    if (value == null)
                    {
                        continue;
                    }

                    list.Add((propName, new Lazy<IEnumerable<ISourceNode>>(() =>
                        {
                            var propertyList = new List<ISourceNode>();

                            foreach (var item in value)
                            {
                                if (item is IExtensionData extensionData)
                                {
                                    propertyList.Add(new ReflectedSourceNode(extensionData, $"{Location}.{propName}", propName));
                                }
                                else
                                {
                                    propertyList.Add(FhirStringSourceNode(thisProp, propName));
                                }
                            }

                            return propertyList;
                        })));
                }
                else
                {
                    list.Add((propName, new Lazy<IEnumerable<ISourceNode>>(() => [FhirStringSourceNode(thisProp, propName)])));
                }
            }

            return list;
        });

        FhirStringSourceNode FhirStringSourceNode(PropertyDescriptor thisProp, string propName)
        {
            return new FhirStringSourceNode(
                () =>
                {
                    var value = thisProp.GetValue(Resource);
                    return value switch
                    {
                        null => null,
                        string valueStr => valueStr,
                        _ => PrimitiveTypeConverter.ConvertTo<string>(value),
                    };
                },
                propName,
                $"{Location}.{propName}");
        }
    }

    public override string Name { get; }

    public override string Text { get; }

    public override string Location { get; }

    public override string ResourceType { get; }

    protected override IEnumerable<(string Name, Lazy<IEnumerable<ISourceNode>> Node)> PropertySourceNodes()
    {
        return _propertySourceNodes.Value;
    }
}
