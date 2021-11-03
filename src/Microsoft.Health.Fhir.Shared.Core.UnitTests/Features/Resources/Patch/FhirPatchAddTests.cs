using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources.Patch
{
    public class FhirPatchAddTests
    {
        // HL7 published tests

        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L78
        /// </summary>
        [Fact]
        public void AddPrimative()
        {
            // Arrange
            var patchParam = new Parameters().AddAddPatchParameter("Patient", "birthDate", new Date("1930-01-01"));

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(new Patient()).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    BirthDate = "1930-01-01"
                }
            ));
        }

        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L297
        /// </summary>
        [Fact]
        public void AddNestedPrimative()
        {
            // Arrange
            var patchParam = new Parameters()
                .AddAddPatchParameter("Patient.contact[0]", "gender", new Code("male"));
            var patientResource = new Patient
            {
                Contact = new List<Patient.ContactComponent>
                {
                    new Patient.ContactComponent { Name = new HumanName() { Text = "a name" } }
                }
            };

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Contact = new List<Patient.ContactComponent>
                    {
                        new Patient.ContactComponent {
                            Name = new HumanName { Text = "a name" },
                            Gender = AdministrativeGender.Male
                        }
                    }
                }
            ));
        }

        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L343
        /// </summary>
        [Fact]
        public void AddComplex()
        {
            // Arrange
            var patchParam = new Parameters().AddAddPatchParameter("Patient", "maritalStatus", new CodeableConcept { Text = "married" });

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(new Patient()).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    MaritalStatus = new CodeableConcept { Text = "married" }
                }
            ));
        }

        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L450
        /// </summary>
        [Fact]
        public void AddAnonymousType()
        {
            // Arrange
            var patchParam = new Parameters().AddAddPatchParameter("Patient", "contact", null);
            patchParam.Parameter[0].Part[3] = new Parameters.ParameterComponent
            {
                Name = "value",
                Part = new List<Parameters.ParameterComponent>
                {
                    new Parameters.ParameterComponent
                    {
                        Name = "name",
                        Value = new HumanName
                        {
                            Text = "a name"
                        }
                    }
                }
            };

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(new Patient()).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Contact = new List<Patient.ContactComponent>
                    {
                        new Patient.ContactComponent
                        {
                            Name = new HumanName
                            {
                                Text = "a name"
                            }
                        }
                    }
                }
            ));
        }

        // Custom Tests

        /// <summary>
        /// Tests "Add" by using the "where" operation
        /// </summary>
        [Fact]
        public void AddNestedPrimativeUsingWhere()
        {
            // Arrange
            var now = DateTimeOffset.Now;
            var patchParam = new Parameters().AddAddPatchParameter("Patient.identifier.where(use = 'official')", "period", new Period { EndElement = new FhirDateTime(now) });
            var patientResource = new Patient
            {
                Identifier = { new Identifier() { Use = Identifier.IdentifierUse.Official, Value = "123" } }
            };

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Identifier = { new Identifier() { Use = Identifier.IdentifierUse.Official, Value = "123", Period = new Period { EndElement = new FhirDateTime(now) } } }
                }
            ));
        }

        /// <summary>
        /// Add is called out as the way to inset at the end of an array
        /// </summary>
        [Fact]
        public void InsertAtEndOfArrayWithAdd()
        {
            // Arrange
            var patientResource = new Patient
            {
                Name = {
                    new HumanName { Given = new[] { "Chad" }, Family = "Johnson", Use = HumanName.NameUse.Old },
                    new HumanName { Given = new[] { "Chad" }, Family = "Ochocinco", Use = HumanName.NameUse.Old }
                }
            };
            var newName = new HumanName { Given = new[] { "Chad", "Ochocinco" }, Family = "Johnson", Use = HumanName.NameUse.Usual };
            var patchParam = new Parameters().AddAddPatchParameter("Patient", "name", newName);

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Name = {
                        new HumanName { Given = new[] { "Chad" }, Family = "Johnson", Use = HumanName.NameUse.Old },
                        new HumanName { Given = new[] { "Chad" }, Family = "Ochocinco", Use = HumanName.NameUse.Old },
                        new HumanName { Given = new[] { "Chad", "Ochocinco" }, Family = "Johnson", Use = HumanName.NameUse.Usual }
                    }
                }
            ));
        }

        /// <summary>
        /// Same as AddAnonymousType but with more complex input
        /// </summary>
        [Fact]
        public void AddComplexAnonymousType()
        {
            // Arrange
            var patchParam = new Parameters().AddAddPatchParameter("Patient", "contact", null);
            patchParam.Parameter[0].Part[3] = new Parameters.ParameterComponent
            {
                Name = "value",
                Part = new List<Parameters.ParameterComponent>
                {
                    new Parameters.ParameterComponent
                    {
                        Name = "name",
                        Value = new HumanName
                        {
                            Given = new List<string> { "a" },
                            Family = "name",
                            Text = "a name",
                            Period = new Period { End = "2020-01-01" }
                        }
                    }
                }
            };

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(new Patient()).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Contact = new List<Patient.ContactComponent>
                    {
                        new Patient.ContactComponent
                        {
                            Name = new HumanName
                            {
                                Given = new List<string> { "a" },
                                Family = "name",
                                Text = "a name",
                                Period = new Period { End = "2020-01-02" }
                            }
                        }
                    }
                }
            ));
        }

        // Proper handling of this use case unclear. Commenting out until more clairty is obtained.
        //[Fact]
        //public void AddNestedPrimativeNullObject()
        //{
        //    // Arrange
        //    var now = DateTimeOffset.Now;
        //    var patchParam = new Parameters().AddAddPatchParameter("Patient.identifier.where(use = 'official').period", "end", new FhirDateTime(now));
        //    var patientResource = new Patient
        //    {
        //        Identifier = { new Identifier() { Use = Identifier.IdentifierUse.Official, Value = "123" } }
        //    };

        //    // Act
        //    Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Add(patchParam.Parameter[0]).Apply();

        //    // Assert
        //    Assert.True(patchedPatientResource.Matches(
        //        new Patient
        //        {
        //            Identifier = { new Identifier() { Use = Identifier.IdentifierUse.Official, Value = "123", Period = new Period { EndElement = new FhirDateTime(now) }}}
        //        }
        //    ));
        //}
    }
}