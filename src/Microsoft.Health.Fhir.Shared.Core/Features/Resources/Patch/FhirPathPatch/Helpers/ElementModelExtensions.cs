// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Specification;
using Hl7.FhirPath;
using static Hl7.Fhir.Model.Parameters;

namespace FhirPathPatch.Helpers
{
    internal static class ElementModelExtensions
    {
        // Converts a resource to an ElementNode
        internal static ElementNode ToElementNode(this Resource resource) =>
            ElementNode.FromElement(resource.ToTypedElement());

        // Finds a child ElementNode at a given path
        internal static ElementNode Find(this ElementNode element, string pathExpression) =>
            ((ScopedNode)element.Select(pathExpression).First()).Current as ElementNode;

        // Finds a child element node with a given name at a given index
        internal static ElementNode AtIndex(this ElementNode element, string elementName, int index) =>
            element.Children(elementName).ElementAt(index).ToElementNode();

        // Converts an ITypedElement to an ElementNode
        internal static ElementNode ToElementNode(this ITypedElement element)
        {
            if (element is ElementNode)
            {
                return element as ElementNode;
            }

            return ElementNode.FromElement(element);
        }

        // Converts a DataType to an ElementNode
        internal static ElementNode ToElementNode(this DataType data) =>
            ElementNode.FromElement(TypedSerialization.ToTypedElement(data));

        // #TODO - insulate this from ParameterComponent
        internal static ElementNode ToElementNode(this object data)
        {
            if (data is DataType dataType)
            {
                return dataType.ToElementNode();
            }

            if (data is ParameterComponent dataComponent)
            {
                var provider = new PocoStructureDefinitionSummaryProvider();
                var node = ElementNode.Root(provider, string.Empty);
                node.Add(provider, dataComponent.Value.ToElementNode(), dataComponent.Name);

                return node;
            }

            throw new ArgumentException("Input data must be of type DataType or ParameterComponent.");
        }
    }
}
