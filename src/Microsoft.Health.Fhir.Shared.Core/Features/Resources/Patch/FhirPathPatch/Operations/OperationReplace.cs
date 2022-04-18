// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch.Operations
{
    /// <summary>
    /// This handles patching an object that requires replacing an element from the resource.
    /// </summary>
    internal class OperationReplace : OperationBase
    {
        internal OperationReplace(Resource resource, PendingOperation po)
            : base(resource, po)
        {
        }

        /// <summary>
        /// Executes a FHIRPath Patch Replace operation. Add operations will
        /// replace the element located at operation.Path with a new value
        /// specified at operation.Value.
        ///
        /// Fhir package has a built-in "ReplaceWith" operation which
        /// accomplishes this easily.
        /// </summary>
        /// <returns>Patched FHIR Resource as POCO.</returns>
        internal override Resource Execute()
        {
            Target.ReplaceWith(Provider, ValueElementNode);
            return ResourceElement.ToPoco<Resource>();
        }
    }
}
