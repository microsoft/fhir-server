// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FhirPathPatch.Helpers;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;

namespace FhirPathPatch.Operations
{
    /// <summary>
    /// This handles patching an object that requires adding a new element.
    /// </summary>
    public class OperationAdd : OperationBase, IOperation
    {
        public OperationAdd(Resource resource)
            : base(resource)
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
        /// <param name="operation">PendingOperation representing Add operation.</param>
        /// <returns>Patched FHIR Resource as POCO.</returns>
        public override Resource Execute(PendingOperation operation)
        {
            var patchPath = ResourceElement.Find(operation.Path);
            patchPath.Add(PocoProvider, operation.Value.ToElementNode(), operation.Name);

            return ResourceElement.ToPoco<Resource>();
        }
    }
}
