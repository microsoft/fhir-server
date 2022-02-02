// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.FhirPath;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch.Operations
{
    /// <summary>
    /// This class handles the Insert operation for FHIR Patch.
    /// </summary>
    internal class OperationInsert : OperationBase
    {
        public OperationInsert(Resource resource, PendingOperation po)
            : base(resource, po)
        {
        }

        /// <summary>
        /// Executes a FHIRPath Patch Insert operation. Insert operations will
        /// add a new element to a list at the specified operation.Path with the
        /// index operation.index.
        ///
        /// Fhir package does NOT have a built-in operation which accomplishes
        /// this. So we must inspect the existing list and recreate it with the
        /// correct elements in order.
        /// </summary>
        /// <returns>Patched FHIR Resource as POCO.</returns>
        internal override Resource Execute()
        {
            // Setup;
            var targetParent = Target.Parent;
            var name = Target.Name;
            var listElements = targetParent.Children(name).ToList()
                                              .Select(x => x as ElementNode)
                                              .Select((value, index) => (value, index))
                                              .ToList();

            // Ensure index is in bounds
            if (Operation.Index < 0 || Operation.Index > listElements.Count)
            {
                throw new InvalidOperationException("Insert index out of bounds of target list");
            }

            // There is no easy "insert" operation in the FHIR library, so we must
            // iterate over the list and recreate it.
            foreach (var child in listElements)
            {
                // Add the new item at the correct index
                if (Operation.Index == child.index)
                {
                    targetParent.Add(Provider, ValueElementNode);
                }

                // Remove the old element from the list so the new order is used
                if (!targetParent.Remove(child.value))
                {
                    throw new InvalidOperationException();
                }

                // Add the old element back to the list
                targetParent.Add(Provider, child.value, name);
            }

            return ResourceElement.ToPoco<Resource>();
        }
    }
}
