using System;
using System.Linq;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Xunit;

namespace FhirPathPatch.UnitTests
{
    public class InsertTest
    {
        [Fact]
        public void SimpleInsertTest()
        {
            // Arrange
            var patientResource = new Patient
            {
                Id = "SimpleInsertTest",
                Name = {
                    new HumanName { Given = new[] { "Chad" }, Family = "Johnson", Use = HumanName.NameUse.Old },
                    new HumanName { Given = new[] { "Chad" }, Family = "Ochocinco", Use = HumanName.NameUse.Old }
                }
            };
            var patchParam = new Parameters();
            var newName = new HumanName { Given = new[] { "Chad", "Ochocinco" }, Family = "Johnson", Use = HumanName.NameUse.Usual };
            patchParam.AddInsertPatchParameter("Patient.name", newName, 0);

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.Equal("Chad", patchedPatientResource.Name.First().Given.First());
            Assert.Equal("Ochocinco", patchedPatientResource.Name.First().Given.ElementAt(1));
            Assert.Equal("Johnson", patchedPatientResource.Name.First().Family);
        }
    }
}
