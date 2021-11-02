using System.Linq;
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
        /// <inheritdoc/>
        public OperationAdd(Resource resource)
            : base(resource) { }

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
            var patchPath = this.ResourceElement.Find(operation.Path);
            patchPath.Add(this.PocoProvider, operation.Value.ToElementNode(), operation.Name);

            return this.ResourceElement.ToPoco<Resource>();
        }
    }
}
