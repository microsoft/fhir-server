// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch.Helpers;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch.Operations
{
    /// <summary>
    /// This handles patching an object that requires moving an element from the resource to a new path.
    /// </summary>
    internal class OperationMove : OperationBase
    {
        internal OperationMove(Resource resource, PendingOperation po)
            : base(resource, po)
        {
        }

        /// <summary>
        /// Executes a FHIRPath Patch Move operation. Move operations will
        /// move an existing element inside a list at the specified
        /// operation.Path from the index of operation.Source to the index of
        /// operation.Destination.
        ///
        /// Fhir package does NOT have a built-in operation which accomplishes
        /// this. So we must inspect the existing list and recreate it with the
        /// correct elements in order.
        /// </summary>
        /// <returns>Patched FHIR Resource as POCO.</returns>
        internal override Resource Execute()
        {
            // Setup
            ElementNode targetParent;
            string name;
            try
            {
                Target = ResourceElement.FindSingleOrCollection(Operation.Path);
                targetParent = Target.Parent;
                name = Target.Name;
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException($"{ex.Message} when processing patch move operation.");
            }

            // Check indexes
            var targetLen = targetParent.Children(name).Count();
            if (Operation.Source < 0 || Operation.Source >= targetLen)
            {
                throw new InvalidOperationException($"Source {Operation.Source} out of bounds when processing patch move operation.");
            }

            if (Operation.Destination < 0 || Operation.Destination >= targetLen)
            {
                throw new InvalidOperationException($"Destination {Operation.Destination} out of bounds when processing patch move operation.");
            }

            // Remove specified element from the list
            var elementToMove = targetParent.AtIndex(name, Operation.Source ?? -1);
            if (!targetParent.Remove(elementToMove))
            {
                throw new InvalidOperationException();
            }

            // There is no easy "move" operation in the FHIR library, so we must
            // iterate over the list to reconstruct it.
            var children = targetParent.Children(name).ToList()
                                       .Select(x => x.ToElementNode())
                                       .Select((value, index) => (value, index));

            foreach (var child in children)
            {
                // Add the new item at the correct index
                if (Operation.Destination == child.index)
                {
                    targetParent.Add(Provider, elementToMove, name);
                }

                // Remove the old element from the list so the new order is used
                if (!targetParent.Remove(child.value))
                {
                    throw new InvalidOperationException();
                }

                // Add the old element back to the list
                targetParent.Add(Provider, child.value, name);
            }

            // Insert if destination is at end of list (orig list length + 1)
            if (children.Count() == Operation.Destination)
            {
                targetParent.Add(Provider, elementToMove, name);
            }

            return ResourceElement.ToPoco<Resource>();
        }
    }
}
