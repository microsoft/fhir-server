using System;
using System.Collections.Generic;
using FhirPathPatch.Operations;
using Hl7.Fhir.Model;
using static Hl7.Fhir.Model.Parameters;

namespace FhirPathPatch
{
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

        public FhirPathPatchBuilder Add(ParameterComponent op)
        {
            this.operations.Add(PendingOperation.FromParameterComponent(op));

            return this;
        }

        public FhirPathPatchBuilder Insert(ParameterComponent op)
        {
            this.operations.Add(PendingOperation.FromParameterComponent(op));

            return this;
        }

        public FhirPathPatchBuilder Delete(ParameterComponent op)
        {
            this.operations.Add(PendingOperation.FromParameterComponent(op));

            return this;
        }

        public FhirPathPatchBuilder Replace(ParameterComponent op)
        {
            this.operations.Add(PendingOperation.FromParameterComponent(op));

            return this;
        }

        public FhirPathPatchBuilder Move(ParameterComponent op)
        {

            this.operations.Add(PendingOperation.FromParameterComponent(op));

            return this;
        }

        public FhirPathPatchBuilder Build(Parameters parameters)
        {
            foreach (var param in parameters.Parameter)
                this.operations.Add(PendingOperation.FromParameterComponent(param));

            return this;
        }
    }
}