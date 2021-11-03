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
        internal static ElementNode ToElementNode(this Resource resource) =>
            ElementNode.FromElement(resource.ToTypedElement());

        internal static ElementNode Find(this ElementNode element, string pathExpression) =>
            ((ScopedNode)element.Select(pathExpression).First()).Current as ElementNode;

        internal static ElementNode AtIndex(this ElementNode element, string elementName, int index) =>
            element.Children(elementName).ElementAt(index).ToElementNode();

        internal static ElementNode ToElementNode(this ITypedElement element)
        {
            if (element is ElementNode) return element as ElementNode;
            return ElementNode.FromElement(element);
        }
        
        internal static ElementNode ToElementNode(this DataType data) =>
            ElementNode.FromElement(TypedSerialization.ToTypedElement(data));

        // #TODO - insulate this from ParameterComponent
        internal static ElementNode ToElementNode(this object data)
        {
            if (data is DataType dataType)
                return dataType.ToElementNode();

            if (data is ParameterComponent dataComponent)
            {
                var provider = new PocoStructureDefinitionSummaryProvider();
                var node = ElementNode.Root(provider, "");
                node.Add(provider, dataComponent.Value.ToElementNode(), dataComponent.Name);

                return node;
            }

            throw new Exception();
        }
    }
}