using System.Linq;
using FhirPathPatch.Helpers;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.FhirPath;
using static Hl7.Fhir.Model.Parameters;

namespace FhirPathPatch.Operations
{
    /// <summary>
    /// This handles patching an object that requires adding an element from the resource.
    /// </summary>
    public class OperationDelete : OperationBase, IOperation
    {
        /// <inheritdoc/>
        public OperationDelete(Resource resource)
            : base(resource) { }

        /// <summary>
        /// Executes a FHIRPath Patch Delete operation. Delete operations will
        /// remove the element at the specified operation.Path.
        ///
        /// Deletion at a non-existing path is not supposed to result in an error.
        ///
        /// Fhir package has a built-in "Remove" operation which accomplishes
        /// this easily.
        /// </summary>
        /// <param name="operation">PendingOperation representing Delete operation.</param>
        /// <returns>Patched FHIR Resource as POCO.</returns>
        public override Resource Execute(PendingOperation operation)
        {
            var targetElement = this.ResourceElement.Find(operation.Path);
            targetElement.Parent.Remove(targetElement);

            return this.ResourceElement.ToPoco<Resource>();
        }
    }
}
