// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using FhirPathPatch;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Resources.Patch
{
    public class FhirPatchReplaceTests
    {
        // HL7 published tests

        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L19
        /// </summary>
        [Fact]
        public void ReplacePrimative()
        {
            // Arrange
            var patchParam = new Parameters().AddReplacePatchParameter("Patient.birthDate", new Date("1930-01-01"));
            var origPatient = new Patient { BirthDate = "1920-01-01" };

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(origPatient).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    BirthDate = "1930-01-01",
                }));
        }

        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L140
        /// </summary>
        [Fact]
        public void ReplaceNestedPrimative1()
        {
            // Arrange
            var patchParam = new Parameters().AddReplacePatchParameter("Patient.contact[0].gender", new Code("female"));
            var origPatient = new Patient
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
            };

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(origPatient).Add(patchParam.Parameter[0]).Apply();

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
                            Gender = AdministrativeGender.Female,
                        },
                     },
                 }));
        }

        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L183
        /// </summary>
        [Fact]
        public void ReplaceNestedPrimative2()
        {
            // Arrange
            var patchParam = new Parameters().AddReplacePatchParameter("Patient.contact[0].name.text", new FhirString("the name"));
            var origPatient = new Patient
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
            };

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(origPatient).Add(patchParam.Parameter[0]).Apply();

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
                                Text = "the name",
                            },
                            Gender = AdministrativeGender.Male,
                        },
                    },
                }));
        }

        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L382
        /// </summary>
        [Fact]
        public void ReplaceComplex()
        {
            // Arrange
            var patchParam = new Parameters().AddReplacePatchParameter("maritalStatus", new CodeableConcept { ElementId = "2", Text = "not married" });
            var origPatient = new Patient
            {
                MaritalStatus = new CodeableConcept
                {
                    ElementId = "1",
                    Text = "married",
                },
            };

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(origPatient).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    MaritalStatus = new CodeableConcept
                    {
                        ElementId = "2",
                        Text = "not married",
                    },
                }));
        }

        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L558
        /// </summary>
        [Fact]
        public void ReplaceListContents()
        {
            // Arrange
            var patchParam = new Parameters()
                .AddReplacePatchParameter("Patient.identifier[0].value", new FhirString("value 2"))
                .AddReplacePatchParameter("Patient.identifier[1].value", new FhirString("value 1"));
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { ElementId = "a", System = "http://example.org", Value = "value 1" },
                        new Identifier { ElementId = "b", System = "http://example.org", Value = "value 2" },
                    },
            };

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource)
                .Add(patchParam.Parameter[0])
                .Add(patchParam.Parameter[1])
                .Apply();

            // Assert
            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Identifier = new List<Identifier>
                    {
                        new Identifier { ElementId = "a", System = "http://example.org", Value = "value 2" },
                        new Identifier { ElementId = "b", System = "http://example.org", Value = "value 1" },
                    },
                }));
        }

        // Other tests

        /// <summary>
        /// Tests teh where operation when replacing the contents of a lits item
        /// </summary>
        [Fact]
        public void ReplaceListContentsUsingWhere()
        {
            // Arrange
            var patchParam = new Parameters()
                .AddReplacePatchParameter("Patient.identifier.where(id = 'a').value", new FhirString("value 2"))
                .AddReplacePatchParameter("Patient.identifier.where(id = 'b').value", new FhirString("value 1"));
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { ElementId = "a", System = "http://example.org", Value = "value 1" },
                        new Identifier { ElementId = "b", System = "http://example.org", Value = "value 2" },
                    },
            };

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource)
                .Add(patchParam.Parameter[0])
                .Add(patchParam.Parameter[1])
                .Apply();

            // Assert
            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Identifier = new List<Identifier>
                    {
                        new Identifier { ElementId = "a", System = "http://example.org", Value = "value 2" },
                        new Identifier { ElementId = "b", System = "http://example.org", Value = "value 1" },
                    },
                }));
        }

        /// <summary>
        /// Tests replace operation when an invalid path is given. InvalidOperationException is expected.
        /// </summary>
        [Fact]
        public void ReplaceInvalidPath()
        {
            // Arrange
            var patchParam = new Parameters().AddReplacePatchParameter("Patient.none", new FhirString("nothing"));

            // Act / Assert
            var patchOperation = new FhirPathPatchBuilder(new Patient()).Add(patchParam.Parameter[0]);
            Assert.Throws<InvalidOperationException>(patchOperation.Apply);
        }

        /* Unsure of proper behavior
        /// <summary>
        /// Tests replace operation when a non-matching data type is given.
        /// </summary>
        [Fact]
        public void ReplaceDifferentDataType()
        {
            // Arrange
            var patchParam = new Parameters().AddReplacePatchParameter("Patient.name[0].text", new FhirDecimal(-1));
            var patchPatient = new Patient
            {
                Name = new List<HumanName>
                {
                    new HumanName
                    {
                        Text = "a name"
                    }
                }
            };

            // Act / Assert
            var patchOperation = new FhirPathPatchBuilder(patchPatient).Add(patchParam.Parameter[0]);
            Assert.Throws<InvalidOperationException>(patchOperation.Apply);
        } */
    }
}
