// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Introspection;
using Hl7.Fhir.Model;
using Hl7.Fhir.Specification;
using Hl7.Fhir.Utility;
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
            var node = ElementNode.FromElement(data.ToTypedElement());
            node.Name = name;
            return node;
        }

        // Given the results of a Path Select command, get the first element node
        internal static ElementNode GetFirstElementNode(this IEnumerable<ITypedElement> nodeList) =>
            nodeList.First().ToScopedNode().Current.ToElementNode();

        // Used to check return from "select" operation
        internal static IEnumerable<ITypedElement> CheckNoElements(this IEnumerable<ITypedElement> nodeList)
        {
            if (!nodeList.Any())
            {
                throw new InvalidOperationException($"No content found");
            }

            return nodeList;
        }

        // Used to check return from "select" operation
        internal static IEnumerable<ITypedElement> CheckMultipleElements(this IEnumerable<ITypedElement> nodeList)
        {
            if (nodeList.Count() > 1)
            {
                var firstResultLocation = nodeList.First().Location;
                var expectedLocation = firstResultLocation.Remove(firstResultLocation.LastIndexOf('.'));

                // Multiple results are only allowed on collection types
                if (nodeList.Any(l =>
                    !l.Definition.IsCollection ||
                    !l.Location.StartsWith(expectedLocation, StringComparison.InvariantCultureIgnoreCase)))
                {
                    throw new InvalidOperationException($"Multiple matches found");
                }
            }

            return nodeList;
        }

        // Used to check return from "select" operation
        internal static IEnumerable<ITypedElement> CheckMultipleElementsOrCollection(this IEnumerable<ITypedElement> nodeList)
        {
            if (nodeList.Count() > 1)
            {
                throw new InvalidOperationException($"Multiple matches found");
            }

            return nodeList;
        }

        // Finds a child element node with a given name at a given index
        internal static ElementNode AtIndex(this ElementNode element, string elementName, int index) =>
            element.Children(elementName).ElementAt(index).ToElementNode();

        // Extracts an ElementNode given a parameter, element name, and expected type.
        // Inspired by:
        // https://github.com/FirelyTeam/spark/blob/795d79e059c751d029257ec6d22da5be850ee016/src/Spark.Engine/Service/FhirServiceExtensions/PatchService.cs#L102
        internal static ElementNode GetElementNodeFromPart(this ParameterComponent part, PropertyMapping partFhirMapping)
        {
            if (part.Value is null)
            {
                var provider = ModelInfoProvider.Instance.StructureDefinitionSummaryProvider;
                var node = ElementNode.Root(provider, partFhirMapping.Name);

                // Group and loop over parts for the same target parameter
                foreach (var group in part.Part.GroupBy(x => x.Name))
                {
                    foreach (var partValue in group)
                    {
                        // Recurese until we find a Primative Type
                        var propMap = partFhirMapping.GetChildMapping(group.Key);
                        var value = partValue.GetElementNodeFromPart(propMap);
                        node.Add(provider, value);
                    }
                }

                return node;
            }

            if (part.Value is DataType valueDataType)
            {
                foreach (var t in partFhirMapping.FhirType)
                {
                    if (valueDataType.GetType().CanBeTreatedAsType(t))
                    {
                        return valueDataType.ToElementNode(partFhirMapping.Name);
                    }
                }
            }

            throw new InvalidOperationException($"Invalid input for {partFhirMapping.Name}");
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
