// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch.Helpers;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch.Operations
{
    /// <summary>
    /// This handles patching an object that requires updating the existing element or adding a new element if it doesn't exists.
    /// </summary>
    internal class OperationUpsert : OperationBase
    {
        internal OperationUpsert(Resource resource, PendingOperation po)
            : base(resource, po)
        {
        }

        /// <summary>
        /// Executes a FHIRPath Patch Add operation if the element doesn't exists. Upsert operations will add a new
        /// element of the specified opearation.Name at the specified operation.Path.
        ///
        /// Adding of a non-existent element will create the new element. Add on
        /// a list will append the list.
        ///
        /// Executes a FHIRPath Patch Replace operation if the element already exists. Upsert operations will
        /// replace the element located at operation.Path with a new value
        /// specified at operation.Value.
        ///
        /// Fhir package has a built-in "Add" and "Upsert" operation which accomplishes this
        /// easily.
        /// </summary>
        /// <returns>Patched FHIR Resource as POCO.</returns>
        internal override Resource Execute()
        {
            try
            {
                // Cache the result of selecting the path
                var pathElements = ResourceElement.Select(Operation.Path).RequireOneOrMoreElements();
                Target = pathElements.GetFirstElementNode();

                if (!Target.Select(Operation.Name).Any())
                {
                    // Add the new element
                    Target = pathElements.RequireMultipleElementsInSameCollection().GetFirstElementNode();
                    var valueElementNodeToAdd = Operation.Value.GetElementNodeFromPart(Target.Definition.GetChildMapping(Operation.Name));
                    Target.Add(Provider, valueElementNodeToAdd);
                }
                else
                {
                    // Replace the existing element
                    Target = pathElements
                        .GetFirstElementNode()
                        .Select(Operation.Name)
                        .RequireOneOrMoreElements()
                        .RequireSingleElement()
                        .GetFirstElementNode();

                    Target.ReplaceWith(Provider, ValueElementNode);
                }
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException($"{ex.Message} at {Operation.Path} when processing patch upsert operation.", ex);
            }

            return ResourceElement.ToPoco<Resource>();
        }
    }
}
