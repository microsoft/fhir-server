using Hl7.Fhir.Model;
using static Hl7.Fhir.Model.Parameters;

namespace FhirPathPatch.Operations
{
    public interface IOperation
    {
        public Resource Execute(PendingOperation operation);
    }
}
