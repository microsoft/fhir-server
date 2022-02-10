// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch.Operations
{
    /// <summary>
    /// This handles patching an object that requires adding an element from the resource.
    /// </summary>
    internal class OperationDelete : OperationBase
    {
        internal OperationDelete(Resource resource, PendingOperation po)
            : base(resource, po)
        {
        }

        /// <summary>
        /// Executes a FHIRPath Patch Delete operation. Delete operations will
        /// remove the element at the specified operation.Path.
        ///
        /// Deletion at a non-existing path is not supposed to result in an error.
        ///
        /// Fhir package has a built-in "Remove" operation which accomplishes
        /// this easily.
        /// </summary>
        /// <returns>Patched FHIR Resource as POCO.</returns>
        internal override Resource Execute()
        {
            try
            {
                Target.Parent.Remove(Target);
                return ResourceElement.ToPoco<Resource>();
            }
            catch (InvalidOperationException)
            {
                return ResourceElement.ToPoco<Resource>();
            }
        }
    }
}
