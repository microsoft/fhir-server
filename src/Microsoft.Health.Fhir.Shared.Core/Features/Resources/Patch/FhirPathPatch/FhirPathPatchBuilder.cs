// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using FhirPathPatch.Operations;
using Hl7.Fhir.Model;
using static Hl7.Fhir.Model.Parameters;

namespace FhirPathPatch
{
    /// <summary>
    /// Handles patching a FHIR Resource in a builder pattern manner.
    /// </summary>
    public class FhirPathPatchBuilder
    {
        private Resource resource;

        private List<PendingOperation> operations;

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
        public Resource Apply()
        {
            foreach (var po in operations)
            {
                // TODO: this should probably be better abstracted / keylookup
                // here
                switch (po.Type)
                {
                    case EOperationType.ADD:
                        resource = new OperationAdd(resource).Execute(po);
                        break;
                    case EOperationType.INSERT:
                        resource = new OperationInsert(resource).Execute(po);
                        break;
                    case EOperationType.REPLACE:
                        resource = new OperationReplace(resource).Execute(po);
                        break;
                    case EOperationType.DELETE:
                        resource = new OperationDelete(resource).Execute(po);
                        break;
                    case EOperationType.MOVE:
                        resource = new OperationMove(resource).Execute(po);
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }

            return resource;
        }

        /// <summary>
        /// Handles the add operation.
        /// </summary>
        /// <param type="ParameterComponent" name="op"> The operation to execute.</param>
        /// <returns>This <see cref="FhirPathPatchBuilder"/>.</returns>
        public FhirPathPatchBuilder Add(ParameterComponent op)
        {
            operations.Add(PendingOperation.FromParameterComponent(op));
            return this;
        }

        /// <summary>
        /// Handles the insert operation.
        /// </summary>
        /// <param type="ParameterComponent" name="op"> The operation to execute.</param>
        /// <returns>This <see cref="FhirPathPatchBuilder"/>.</returns>
        public FhirPathPatchBuilder Insert(ParameterComponent op)
        {
            operations.Add(PendingOperation.FromParameterComponent(op));
            return this;
        }

        /// <summary>
        /// Handles the delete operation.
        /// </summary>
        /// <param type="ParameterComponent" name="op"> The operation to execute.</param>
        /// <returns>This <see cref="FhirPathPatchBuilder"/>.</returns>
        public FhirPathPatchBuilder Delete(ParameterComponent op)
        {
            operations.Add(PendingOperation.FromParameterComponent(op));
            return this;
        }

        /// <summary>
        /// Handles the Replace operation.
        /// </summary>
        /// <param type="ParameterComponent" name="op"> The operation to execute.</param>
        /// <returns>This <see cref="FhirPathPatchBuilder"/>.</returns>
        public FhirPathPatchBuilder Replace(ParameterComponent op)
        {
            operations.Add(PendingOperation.FromParameterComponent(op));
            return this;
        }

        /// <summary>
        /// Handles the Move operation.
        /// </summary>
        /// <param type="ParameterComponent" name="op"> The operation to execute.</param>
        /// <returns>This <see cref="FhirPathPatchBuilder"/>.</returns>
        public FhirPathPatchBuilder Move(ParameterComponent op)
        {
            operations.Add(PendingOperation.FromParameterComponent(op));
            return this;
        }

        /// <summary>
        /// Builds the list of operations to execute
        /// </summary>
        /// <param type="parameters" name="parameters">The parameters to build the chain of the builder.</param>
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
