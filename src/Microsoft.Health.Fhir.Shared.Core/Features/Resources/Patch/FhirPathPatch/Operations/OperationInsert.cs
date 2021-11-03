using System;
using System.Collections.Generic;
using System.Linq;
using FhirPathPatch.Helpers;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Hl7.Fhir.Specification;
using Hl7.FhirPath;
using static Hl7.Fhir.Model.Parameters;

namespace FhirPathPatch.Operations
{
    public class OperationInsert : OperationBase, IOperation
    {
        public OperationInsert(Resource resource)
            : base(resource) { }

        /// <summary>
        /// Executes a FHIRPath Patch Insert operation. Insert operations will
        /// add a new element to a list at the specified operation.Path with the
        /// index operation.index.
        ///
        /// Fhir package does NOT have a built-in operation which accomplishes
        /// this. So we must inspect the existing list and recreate it with the
        /// correct elements in order.
        /// </summary>
        /// <param name="operation">PendingOperation representing Insert operation.</param>
        /// <returns>Patched FHIR Resource as POCO.</returns>
        public override Resource Execute(PendingOperation operation)
        {
            var targetElement = this.ResourceElement.Find(operation.Path);
            var targetParent = targetElement.Parent;
            var name = targetElement.Name;
            var listElements = targetParent.Children(name).ToList()
                                              .Select(x => x as ElementNode)
                                              .Select((value, index) => (value, index))
                                              .ToList();

            // Ensure index is in bounds
            if (operation.Index < 0 || operation.Index > listElements.Count)
                throw new InvalidOperationException("Insert index out of bounds of target list");

            // There is no easy "insert" operation in the FHIR library, so we must
            // iterate over the list and recreate it.
            foreach (var child in listElements)
            {
                // Add the new item at the correct index
                if (operation.Index == child.index)
                    targetParent.Add(this.PocoProvider, operation.Value.ToElementNode(), name);

                // Remove the old element from the list so the new order is used
                if (!targetParent.Remove(child.value))
                    throw new InvalidOperationException();

                // Add the old element back to the list
                targetParent.Add(this.PocoProvider, child.value, name);
            }

            return this.ResourceElement.ToPoco<Resource>();
        }
    }
}