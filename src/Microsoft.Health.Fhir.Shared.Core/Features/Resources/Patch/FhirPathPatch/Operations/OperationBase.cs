using FhirPathPatch.Helpers;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Specification;

namespace FhirPathPatch.Operations
{
    /// <summary>
    /// Abstract representation of a basic operational resource.
    /// </summary>
    public abstract class OperationBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="OperationBase"/> class.
        /// </summary>
        /// <param name="resource">FHIR Resource for this operation.</param>
        public OperationBase(Resource resource)
        {
            this.PocoProvider = new PocoStructureDefinitionSummaryProvider();
            this.ResourceElement = resource.ToElementNode();
        }

        /// <summary>
        /// Gets the provider used in POCO conversion.
        /// </summary>
        protected PocoStructureDefinitionSummaryProvider PocoProvider { get; }

        /// <summary>
        /// Gets the element node representation of the patch operation
        /// resource.
        /// </summary>
        protected ElementNode ResourceElement { get; }

        /// <summary>
        /// All inheriting classes must implement an operation exeuction method.
        /// </summary>
        /// <param name="operation">Input parameters to Patch operatioin.</param>
        /// <returns>FHIR Resource as POCO.</returns>
        public abstract Resource Execute(PendingOperation operation);
    }
}
