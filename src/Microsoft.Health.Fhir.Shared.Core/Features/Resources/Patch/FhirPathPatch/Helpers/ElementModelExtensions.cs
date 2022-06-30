// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;
using Hl7.Fhir.Specification;
using Hl7.Fhir.Utility;
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

        // Find function for operations that can occur on a list (add, insert, move).
        internal static ElementNode FindSingleOrCollection(this ElementNode element, string pathExpression)
        {
            var resultList = element.Select(pathExpression);

            // Paths must resolve to exactly one element (unless target is collection)
            if (!resultList.Any())
            {
                throw new InvalidOperationException($"No content found at {pathExpression}");
            }
            else if (resultList.Count() > 1)
            {
                var firstResultLocation = resultList.First().Location;
                var expectedLocation = firstResultLocation.Remove(firstResultLocation.LastIndexOf('.'));

                // Multiple results are only allowed on collection types
                if (resultList.Any(l =>
                    !l.Definition.IsCollection ||
                    !l.Location.StartsWith(expectedLocation, StringComparison.InvariantCultureIgnoreCase)))
                {
                    throw new InvalidOperationException($"Multiple matches found for {pathExpression}");
                }
            }

            return resultList.First().ToScopedNode().Current.ToElementNode();
        }

        // Find function for operations that must return a single element (replace).
        internal static ElementNode FindSingle(this ElementNode element, string pathExpression)
        {
            var resultList = element.Select(pathExpression);

            // Paths must resolve to exactly one element
            if (!resultList.Any())
            {
                throw new InvalidOperationException($"No content found at {pathExpression}");
            }
            else if (resultList.Count() > 1)
            {
                throw new InvalidOperationException($"Multiple matches found for {pathExpression}");
            }

            return resultList.Single().ToScopedNode().Current.ToElementNode();
        }

        // Find function for operations that must can return a single element.
        internal static ElementNode FindSingleOrNone(this ElementNode element, string pathExpression)
        {
            var resultList = element.Select(pathExpression);

            // Paths must resolve to exactly one element (unless target is collection)
            if (resultList.Count() > 1)
            {
                throw new InvalidOperationException($"Multiple matches found for {pathExpression}");
            }

            return resultList.Count() == 1 ? resultList.Single().ToScopedNode().Current.ToElementNode() : null;
        }

        // Finds a child element node with a given name at a given index
        internal static ElementNode AtIndex(this ElementNode element, string elementName, int index) =>
            element.Children(elementName).ElementAt(index).ToElementNode();

        // Extracts an ElementNode given a parameter, element name, and expected type.
        // Inspired by:
        // https://github.com/FirelyTeam/spark/blob/795d79e059c751d029257ec6d22da5be850ee016/src/Spark.Engine/Service/FhirServiceExtensions/PatchService.cs#L102
        internal static ElementNode GetElementNodeFromPart(this ParameterComponent part, string partName, PropertyMapping partFhirMapping)
        {
            if (part.Value is null)
            {
                var provider = ModelInfoProvider.Instance.StructureDefinitionSummaryProvider;
                var node = ElementNode.Root(provider, partFhirMapping.Name, partName);

                // Group and loop over parts for the same target parameter
                foreach (var group in part.Part.GroupBy(x => x.Name))
                {
                    foreach (var partValue in group)
                    {
                        // Recurese until we find a Primative Type
                        var propMap = partFhirMapping.GetChildMapping(group.Key);
                        var value = GetElementNodeFromPart(partValue, group.Key, propMap);
                        node.Add(provider, value);
                    }
                }

                return node;
            }

            if (part.Value is DataType valueDataType)
            {
                foreach (var t in partFhirMapping.FhirType)
                {
                    if (ReflectionHelper.CanBeTreatedAsType(t, valueDataType.GetType()))
                    {
                        return valueDataType.ToElementNode(partName);
                    }
                }
            }

            throw new InvalidOperationException($"Invalid input for {partName}");
        }

        // Gets a child definition given a parent definition and a property name.
        internal static PropertyMapping GetChildMapping(this IElementDefinitionSummary elementSummary, string childPropName)
        {
            PropertyMapping childMap = elementSummary switch
            {
                PropertyMapping p => p.PropertyTypeMapping.FindMappedElementByName(childPropName),
                ElementDefinitionSummary e => (e.Type[0] as ClassMapping).FindMappedElementByName(childPropName),

                // ClassMapping c => c.FindMappedElementByName(childPropName),
                _ => throw new InvalidOperationException("Path must resolve to a resource or element"),
            };

            if (childMap is null)
            {
                throw new InvalidOperationException($"Element {childPropName} not found");
            }

            return childMap;
        }
    }
}
