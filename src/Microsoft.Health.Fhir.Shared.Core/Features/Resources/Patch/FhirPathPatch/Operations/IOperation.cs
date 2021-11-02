using Hl7.Fhir.Model;
using static Hl7.Fhir.Model.Parameters;

namespace FhirPathPatch.Operations
{
    /// <summary>
    /// Patch operations Interface
    /// </summary>
    public interface IOperation
    {
        /// <summary>
        /// <param type="PendingOperation">The patch operation to take place.</param>
        /// <returns>Patched FHIR Resource.</returns>
        /// </summary>
        public Resource Execute(PendingOperation operation);
    }
}
