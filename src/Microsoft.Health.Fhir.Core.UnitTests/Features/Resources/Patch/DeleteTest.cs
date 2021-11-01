using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Xunit;

namespace FhirPathPatch.UnitTests
{
    public class DeleteTest
    {
        [Fact]
        public void SimpleDeleteTest()
        {
            // Arrange
            var patientResource = new Patient
            {
                BirthDate = "1920-01-01"
            };
            var patchParam = new Parameters();
            patchParam.AddDeletePatchParameter("Patient.birthDate");

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.Null(patchedPatientResource.BirthDate);
        }

        [Fact]
        public void NestedDeleteTest()
        {
            // Arrange
            var patientResource = new Patient
            {
                Contact = new List<Patient.ContactComponent>()
                {
                    new Patient.ContactComponent()
                    {
                        Name = new HumanName() { Text = "a name" },
                        Gender = AdministrativeGender.Male
                    }
                }
            };
            var patchParam = new Parameters();
            patchParam.AddDeletePatchParameter("Patient.contact[0].gender");

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.Null(patchedPatientResource.Contact[0].Gender);
            Assert.Equal("a name", patchedPatientResource.Contact[0].Name.Text);
        }
    }
}
