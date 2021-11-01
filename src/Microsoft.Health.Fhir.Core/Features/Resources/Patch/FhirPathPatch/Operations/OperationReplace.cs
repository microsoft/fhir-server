using FhirPathPatch.Helpers;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;

namespace FhirPathPatch.Operations
{
    public class OperationReplace : OperationBase, IOperation
    {
        public OperationReplace(Resource resource)
            : base(resource) { }

        /// <summary>
        /// Executes a FHIRPath Patch Replace operation. Add operations will
        /// replace the element located at operation.Path with a new value
        /// specified at operation.Value.
        ///
        /// Fhir package has a built-in "ReplaceWith" operation which
        /// accomplishes this easily.
        /// </summary>
        /// <param name="operation">PendingOperation representing Replace operation.</param>
        /// <returns>Patched FHIR Resource as POCO.</returns>
        public override Resource Execute(PendingOperation operation)
        {
            var targetElement = this.ResourceElement.Find(operation.Path);
            targetElement.ReplaceWith(this.PocoProvider, operation.Value.ToElementNode());

            return this.ResourceElement.ToPoco<Resource>();
        }
    }
}