// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using FhirPathPatch;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Resources.Patch
{
    public class FhirPatchDeleteTests
    {
        // HL7 published tests

        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L51
        /// </summary>
        [Fact]
        public void DeletePrimative()
        {
            // Arrange
            var patchParam = new Parameters().AddDeletePatchParameter("Patient.birthDate");

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(new Patient { BirthDate = "1920-01-01" }).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.True(patchedPatientResource.Matches(new Patient()));
        }

        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L225
        /// </summary>
        [Fact]
        public void DeleteNestedPrimative()
        {
            // Arrange
            var patchParam = new Parameters().AddDeletePatchParameter("Patient.contact[0].gender");

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(
                new Patient
                {
                    Contact = new List<Patient.ContactComponent>
                    {
                        new Patient.ContactComponent
                        {
                            Name = new HumanName
                            {
                                Text = "a name",
                            },
                            Gender = AdministrativeGender.Male,
                        },
                    },
                }).Add(patchParam.Parameter[0]).Apply();

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
                                Text = "a name",
                            },
                        },
                    },
                }));
        }

        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L262
        /// </summary>
        [Fact]
        public void DeleteNestedPrimative2()
        {
            // Arrange
            var patchParam = new Parameters().AddDeletePatchParameter("Patient.contact[0].name.text");

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(
                new Patient
                {
                    Contact = new List<Patient.ContactComponent>
                    {
                        new Patient.ContactComponent
                        {
                            Name = new HumanName
                            {
                                Text = "a name",
                            },
                            Gender = AdministrativeGender.Male,
                        },
                    },
                }).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Contact = new List<Patient.ContactComponent>
                    {
                        new Patient.ContactComponent
                        {
                            Gender = AdministrativeGender.Male,
                        },
                    },
                }));
        }

        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L420
        /// </summary>
        [Fact]
        public void DeleteComplex()
        {
            // Arrange
            var patchParam = new Parameters().AddDeletePatchParameter("Patient.maritalStatus");

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(
                new Patient
                {
                    MaritalStatus = new CodeableConcept { Text = "married" },
                }).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.True(patchedPatientResource.Matches(new Patient()));
        }

        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L494
        /// </summary>
        [Fact]
        public void DeleteAnonymous()
        {
            // Arrange
            var patchParam = new Parameters().AddDeletePatchParameter("Patient.contact[0]");

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(
                new Patient
                {
                    Contact = new List<Patient.ContactComponent>
                    {
                        new Patient.ContactComponent
                        {
                            Name = new HumanName
                            {
                                Text = "a name",
                            },
                        },
                    },
                }).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.True(patchedPatientResource.Matches(new Patient()));
        }

        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L790
        /// </summary>
        [Fact]
        public void DeleteFromList1()
        {
            // Arrange
            var patchParam = new Parameters().AddDeletePatchParameter("Patient.identifier[0]");
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                    },
            };

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.True(patchedPatientResource.Matches(new Patient
            {
                Identifier = new List<Identifier>
                {
                    new Identifier { System = "http://example.org", Value = "value 2" },
                    new Identifier { System = "http://example.org", Value = "value 3" },
                },
            }));
        }

        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L836
        /// </summary>
        [Fact]
        public void DeleteFromList2()
        {
            // Arrange
            var patchParam = new Parameters().AddDeletePatchParameter("Patient.identifier[1]");
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                    },
            };

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.True(patchedPatientResource.Matches(new Patient
            {
                Identifier = new List<Identifier>
                {
                    new Identifier { System = "http://example.org", Value = "value 1" },
                    new Identifier { System = "http://example.org", Value = "value 3" },
                },
            }));
        }

        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L882
        /// </summary>
        [Fact]
        public void DeleteFromList3()
        {
            // Arrange
            var patchParam = new Parameters().AddDeletePatchParameter("Patient.identifier[2]");
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                    },
            };

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.True(patchedPatientResource.Matches(new Patient
            {
                Identifier = new List<Identifier>
                {
                    new Identifier { System = "http://example.org", Value = "value 1" },
                    new Identifier { System = "http://example.org", Value = "value 2" },
                },
            }));
        }

        // Other tests

        /// <summary>
        /// Tests delete when the path does not resolve. According to
        /// https://www.hl7.org/fhir/fhirpatch.html this is not supposed to
        /// result in an error, but the input object with nothing deleted.
        /// </summary>
        [Fact]
        public void DeletePathDoesntResolve1()
        {
            // Arrange
            var patchParam = new Parameters().AddDeletePatchParameter("Patient.nothing");
            var patchPatient = new Patient
            {
                BirthDate = "1920-01-01",
            };

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patchPatient).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.True(patchedPatientResource.Matches(patchPatient));
        }

        /// <summary>
        /// see above
        /// </summary>
        [Fact]
        public void DeletePathDoesntResolve2()
        {
            // Arrange
            var patchParam = new Parameters().AddDeletePatchParameter("Contact.name");
            var patchPatient = new Patient
            {
                BirthDate = "1920-01-01",
            };

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patchPatient).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.True(patchedPatientResource.Matches(patchPatient));
        }
    }
}
