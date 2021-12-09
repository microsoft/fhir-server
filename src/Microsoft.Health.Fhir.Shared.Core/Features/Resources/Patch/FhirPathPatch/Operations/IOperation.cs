// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;

namespace FhirPathPatch.Operations
{
    /// <summary>
    /// Patch operations Interface
    /// </summary>
    public interface IOperation
    {
        /// <summary>
        /// <param type="PendingOperation" name="operation">The patch operation to take place.</param>
        /// <returns>Patched FHIR Resource.</returns>
        /// </summary>
        public Resource Execute();
    }
}
