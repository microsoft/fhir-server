using System;
using System.Linq;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Xunit;

namespace FhirPathPatch.UnitTests
{
    public class ReplaceTest
    {
        [Fact]
        public void SimpleReplaceTest()
        {
            // Arrange
            var patientResource = new Patient
            {
                Id = "SimpleReplaceTest",
                Name = { new HumanName { Given = new[] { "Chad" }, Family = "Johnson" } },
                Gender = AdministrativeGender.Unknown
            };
            var patchParam = new Parameters();
            patchParam.AddReplacePatchParameter("Patient.gender", new FhirString("female"));

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.Equal(AdministrativeGender.Female, patchedPatientResource.Gender);

        }

        [Fact]
        public void DeepReplaceTest()
        {
            // Arrange
            var patientResource = new Patient
            {
                Id = "DeepReplaceTest",
                Name = { new HumanName { Given = new[] { "Chad" }, Family = "Johnson" } }
            };
            var patchParam = new Parameters();
            patchParam.AddReplacePatchParameter("Patient.name[0].family", new FhirString("OchoCinco"));

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.Equal("OchoCinco", patchedPatientResource.Name[0].Family);
        }

        [Fact]
        public void ArrayReplaceTest()
        {
            // Arrange
            var patientResource = new Patient
            {
                Id = "ArrayReplaceTest",
                Name = { new HumanName { Given = new[] { "Chad" }, Family = "Johnson" } }
            };
            var newName = new HumanName { Given = new[] { "Chad2" }, Family = "OchoCinco" };
            var patchParam = new Parameters();
            patchParam.AddReplacePatchParameter("Patient.name[0]", newName);

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.Equal("Chad2", patchedPatientResource.Name[0].Given.First());
            Assert.Equal("OchoCinco", patchedPatientResource.Name[0].Family);
        }

        [Fact]
        public void ArrayWhereReplaceTest()
        {
            // Arrange
            var patientResource = new Patient
            {
                Id = "ArrayWhereReplaceTest",
                Name = { new HumanName { Given = new[] { "Chad" }, Family = "Johnson", Use = HumanName.NameUse.Usual } }
            };
            var newName = new HumanName { Given = new[] { "Chad2" }, Family = "OchoCinco", Use = HumanName.NameUse.Usual };
            var patchParam = new Parameters();
            patchParam.AddReplacePatchParameter("Patient.name.where(use = 'usual')", newName);

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.Equal("Chad2", patchedPatientResource.Name.First(x => x.Use == HumanName.NameUse.Usual).Given.First());
            Assert.Equal("OchoCinco", patchedPatientResource.Name.First(x => x.Use == HumanName.NameUse.Usual).Family);
        }

    }
}
