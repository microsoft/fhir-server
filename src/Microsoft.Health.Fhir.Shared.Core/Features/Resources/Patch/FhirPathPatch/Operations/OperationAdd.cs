﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;

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
            Target.Add(Provider, ValueElementNode);
            return ResourceElement.ToPoco<Resource>();
        }
    }
}
