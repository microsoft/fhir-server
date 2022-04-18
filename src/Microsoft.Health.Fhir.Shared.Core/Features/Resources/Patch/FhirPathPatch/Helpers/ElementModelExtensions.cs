// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Reflection;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;
using Hl7.Fhir.Specification;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Models;
using static Hl7.Fhir.Model.Parameters;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch.Helpers
{
    internal static class ElementModelExtensions
    {
        // Converts a FHIR Resource to an ElementNode
        internal static ElementNode ToElementNode(this Base b) =>
            ElementNode.FromElement(b.ToTypedElement());

        // Converts an ITypedElement to an ElementNode
        internal static ElementNode ToElementNode(this ITypedElement element) =>
            (element is ElementNode el) ? el : ElementNode.FromElement(element);

        // Converts a DataType to a named ElementNode
        internal static ElementNode ToElementNode(this DataType data, string name)
        {
            var node = ElementNode.FromElement(TypedSerialization.ToTypedElement(data));
            node.Name = name;
            return node;
        }

        // Finds a child ElementNode at a given path
        internal static ElementNode Find(this ElementNode element, string pathExpression) =>
            ((ScopedNode)element.Select(pathExpression).First()).Current as ElementNode;

        // Finds a child element node with a given name at a given index
        internal static ElementNode AtIndex(this ElementNode element, string elementName, int index) =>
            element.Children(elementName).ElementAt(index).ToElementNode();

        // Extracts an ElementNode given a parameter, element name, and expected type.
        // Inspired by:
        // https://github.com/FirelyTeam/spark/blob/795d79e059c751d029257ec6d22da5be850ee016/src/Spark.Engine/Service/FhirServiceExtensions/PatchService.cs#L102
        internal static ElementNode GetElementNodeFromPart(this ParameterComponent part, string partName, Type resultType)
        {
            if (part.Value is null)
            {
                var provider = ModelInfoProvider.Instance.StructureDefinitionSummaryProvider;
                var node = ElementNode.Root(provider, resultType.ToString(), partName);

                // Group and loop over parts for the same target parameter
                foreach (var group in part.Part.GroupBy(x => x.Name))
                {
                    foreach (var partValue in group)
                    {
                        if (resultType.IsGenericType)
                        {
                            resultType = resultType.GenericTypeArguments.First();
                        }

                        var propertyInfo = resultType.GetProperties().Single(
                            p => p.GetCustomAttribute<FhirElementAttribute>()?.Name == group.Key);

                        var value = GetElementNodeFromPart(partValue, group.Key, propertyInfo.PropertyType);
                        node.Add(provider, value);
                    }
                }

                return node;
            }

            if (part.Value is DataType valueDataType)
            {
                if (resultType.IsAssignableFrom(valueDataType.GetType()))
                {
                    return valueDataType.ToElementNode(partName);
                }
            }

            throw new InvalidOperationException();
        }

        // Gets a child definition given a parent definition and a property name.
        internal static PropertyMapping GetChildDefinition(this IElementDefinitionSummary summary, string name) =>
            (summary switch
            {
                PropertyMapping propDefinition => propDefinition.PropertyTypeMapping,
                _ => summary.Type[0] as ClassMapping,
            }).PropertyMappings.Single(x => x.Name == name);
    }
}
