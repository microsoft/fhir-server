// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch.Operations;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch
{
    /// <summary>
    /// Handles patching a FHIR Resource in a builder pattern manner.
    /// </summary>
    public class FhirPathPatchBuilder
    {
        private Resource resource;

        private readonly List<PendingOperation> operations;

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirPathPatchBuilder"/> class.
        /// </summary>
        /// <param name="resource">FHIR Resource.</param>
        public FhirPathPatchBuilder(Resource resource)
        {
            this.resource = resource;
            operations = new List<PendingOperation>();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirPathPatchBuilder"/> class with Patch Parameters.
        /// </summary>
        /// <param name="resource">FHIR Resource.</param>
        /// <param name="parameters">Patch Parameters.</param>
        public FhirPathPatchBuilder(Resource resource, Parameters parameters)
        : this(resource)
        {
            Build(parameters);
        }

        /// <summary>
        /// Applies the list of pending operations to the resource.
        /// </summary>
        /// <returns>FHIR Resource <see cref="Resource"/>.</returns>
        public Resource Apply()
        {
            foreach (var po in operations)
            {
                resource = po.Type switch
                {
                    PatchOperationType.ADD => new OperationAdd(resource, po).Execute(),
                    PatchOperationType.INSERT => new OperationInsert(resource, po).Execute(),
                    PatchOperationType.REPLACE => new OperationReplace(resource, po).Execute(),
                    PatchOperationType.DELETE => new OperationDelete(resource, po).Execute(),
                    PatchOperationType.MOVE => new OperationMove(resource, po).Execute(),
                    _ => throw new NotImplementedException(),
                };
            }

            return resource;
        }

        /// <summary>
        /// Builds the list of operations to execute
        /// </summary>
        /// <param type="parameters" name="parameters">The parameters to build the chain of the builder.</param>
        /// <returns>This <see cref="FhirPathPatchBuilder"/>.</returns>
        public FhirPathPatchBuilder Build(Parameters parameters)
        {
            foreach (var param in parameters.Parameter)
            {
                operations.Add(PendingOperation.FromParameterComponent(param));
            }

            return this;
        }
    }
}
