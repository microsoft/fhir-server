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
    /// This handles patching an object that requires adding a new element.
    /// </summary>
    internal class OperationAdd : OperationBase
    {
        internal OperationAdd(Resource resource, PendingOperation po)
            : base(resource, po)
        {
        }

        /// <summary>
        /// Gets the value of the patch operation as an ElementNode.
        /// </summary>
        internal override ElementNode ValueElementNode =>
            Operation.Value.GetElementNodeFromPart(Target.Definition.GetChildMapping(Operation.Name));

        /// <summary>
        /// Executes a FHIRPath Patch Add operation. Add operations will add a new
        /// element of the specified opearation.Name at the specified operation.Path.
        ///
        /// Adding of a non-existent element will create the new element. Add on
        /// a list will append the list.
        ///
        /// Fhir package has a built-in "Add" operation which accomplishes this
        /// easily.
        /// </summary>
        /// <returns>Patched FHIR Resource as POCO.</returns>
        internal override Resource Execute()
        {
            try
            {
                Target = ResourceElement
                            .Select(Operation.Path)
                            .RequireOneOrMoreElements()
                            .RequireMultipleElementsInSameCollection()
                            .GetFirstElementNode();

                // Check to ensure we aren't adding over an exsting element
                var targetAtName = Target.Select(Operation.Name);
                if (targetAtName.Any(x => !x.Definition.IsCollection))
                {
                    throw new InvalidOperationException($"Existing element {Operation.Name} found");
                }

                Target.Add(Provider, ValueElementNode);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException($"{ex.Message} at {Operation.Path} when processing patch add operation.");
            }

            return ResourceElement.ToPoco<Resource>();
        }
    }
}
