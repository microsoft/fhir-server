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

            this.operations = new List<PendingOperation>();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirPathPatchBuilder"/> class with Patch Parameters.
        /// </summary>
        /// <param name="resource">FHIR Resource.</param>
        /// <param name="parameters">Patch Parameters.</param>
        public FhirPathPatchBuilder(Resource resource, Parameters parameters)
        : this(resource)
        {
            this.Build(parameters);
        }

        /// <summary>
        /// Applies the list of pending operations to the resource.
        /// </summary>
        public Resource Apply()
        {
            Resource workingResource = new Patient();
            this.resource.CopyTo(workingResource);
            foreach (var po in this.operations)
            {
                // TODO: this should probably be better abstracted / keylookup
                // here
                switch (po.Type)
                {
                    case EOperationType.ADD:
                        workingResource = new OperationAdd(workingResource).Execute(po);
                        break;
                    case EOperationType.INSERT:
                        workingResource = new OperationInsert(workingResource).Execute(po);
                        break;
                    case EOperationType.REPLACE:
                        workingResource = new OperationReplace(workingResource).Execute(po);
                        break;
                    case EOperationType.DELETE:
                        workingResource = new OperationDelete(workingResource).Execute(po);
                        break;
                    case EOperationType.MOVE:
                        workingResource = new OperationMove(workingResource).Execute(po);
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }

            return workingResource;
        }

        // <summary>
        /// Handles the add operation.
        /// </summary>
        /// <param type="ParameterComponent"> The operation to execute.</param>
        /// <returns>This <see cref="FhirPathPatchBuilder"/>.</returns>
        public FhirPathPatchBuilder Add(ParameterComponent op)
        {
            this.operations.Add(PendingOperation.FromParameterComponent(op));

            return this;
        }

        /// <summary>
        /// Handles the insert operation.
        /// </summary>
        /// <param type="ParameterComponent"> The operation to execute.</param>
        /// <returns>This <see cref="FhirPathPatchBuilder"/>.</returns>
        public FhirPathPatchBuilder Insert(ParameterComponent op)
        {
            this.operations.Add(PendingOperation.FromParameterComponent(op));

            return this;
        }

        /// <summary>
        /// Handles the delete operation.
        /// </summary>
        /// <param type="ParameterComponent"> The operation to execute.</param>
        /// <returns>This <see cref="FhirPathPatchBuilder"/>.</returns>
        public FhirPathPatchBuilder Delete(ParameterComponent op)
        {
            this.operations.Add(PendingOperation.FromParameterComponent(op));

            return this;
        }

        /// <summary>
        /// Handles the Replace operation.
        /// </summary>
        /// <param type="ParameterComponent"> The operation to execute.</param>
        /// <returns>This <see cref="FhirPathPatchBuilder"/>.</returns>
        public FhirPathPatchBuilder Replace(ParameterComponent op)
        {
            this.operations.Add(PendingOperation.FromParameterComponent(op));

            return this;
        }

        /// <summary>
        /// Handles the Move operation.
        /// </summary>
        /// <param type="ParameterComponent"> The operation to execute.</param>
        /// <returns>This <see cref="FhirPathPatchBuilder"/>.</returns>
        public FhirPathPatchBuilder Move(ParameterComponent op)
        {

            this.operations.Add(PendingOperation.FromParameterComponent(op));

            return this;
        }

        /// <summary>
        /// Builds the list of operations to execute
        /// </summary>
        /// <param type="parameters">The parameters to build the chain of the builder.</param>
        public FhirPathPatchBuilder Build(Parameters parameters)
        {
            foreach (var param in parameters.Parameter)
                this.operations.Add(PendingOperation.FromParameterComponent(param));

            return this;
        }
    }
}