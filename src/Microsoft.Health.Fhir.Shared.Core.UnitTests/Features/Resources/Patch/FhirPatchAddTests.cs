// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch.Helpers;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Resources.Patch
{
    public class FhirPatchAddTests
    {
        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L78
        [Fact]
        public void GivenAFhirPatchAddRequest_WhenAddingPrimitiveVaue_ThenPrimitiveShouldBePopulated()
        {
            var patchParam = new Parameters().AddAddPatchParameter("Patient", "birthDate", new Date("1930-01-01"));

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(new Patient(), patchParam).Apply();

            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    BirthDate = "1930-01-01",
                }));
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L297
        [Fact]
        public void GivenAFhirPatchAddRequest_WhenAddingNestedPrimitiveValue_ThenNestedPrimitiveShouldBePopulated()
        {
            var patchParam = new Parameters()
                .AddAddPatchParameter("Patient.contact[0]", "gender", new Code("male"));
            var patientResource = new Patient
            {
                Contact = new List<Patient.ContactComponent>
                {
                    new Patient.ContactComponent { Name = new HumanName() { Text = "a name" } },
                },
            };

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource, patchParam).Apply();

            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Contact = new List<Patient.ContactComponent>
                    {
                        new Patient.ContactComponent
                        {
                            Name = new HumanName { Text = "a name" },
                            Gender = AdministrativeGender.Male,
                        },
                    },
                }));
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L343
        [Fact]
        public void GivenAFhirPatchAddRequest_WhenAddingComplexValue_ThenComplexShouldBePopulated()
        {
            var patchParam = new Parameters().AddAddPatchParameter("Patient", "maritalStatus", new CodeableConcept { Text = "married" });

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(new Patient(), patchParam).Apply();

            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    MaritalStatus = new CodeableConcept { Text = "married" },
                }));
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L450
        [Fact]
        public void GivenAFhirPatchAddRequest_WhenAddingAnAnonymousObject_ThenObjectShouldExistOnResource()
        {
            var patchParam = new Parameters().AddPatchParameter("add", path: "Patient", name: "contact", value: new Parameters.ParameterComponent
                {
                    Name = "name",
                    Value = new HumanName
                    {
                        Text = "a name",
                    },
                });

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(new Patient(), patchParam).Apply();

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

        [Fact]
        public void GivenAFhirPatchAddRequest_WhenAddingNestedPrimitiveVaue_ThenNestedPrimitiveShouldBePopulated()
        {
            var patchParam = new Parameters().AddAddPatchParameter("Patient.identifier.where(use = 'official')", "period", new Period { EndElement = new FhirDateTime("2021-12-01") });
            var patientResource = new Patient
            {
                Identifier = { new Identifier() { Use = Identifier.IdentifierUse.Official, Value = "123" } },
            };

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource, patchParam).Apply();

            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Identifier = { new Identifier() { Use = Identifier.IdentifierUse.Official, Value = "123", Period = new Period { EndElement = new FhirDateTime("2021-12-01") } } },
                }));
        }

        [Fact]
        public void GivenAFhirPatchAddRequest_WhenAddingToArray_ThenObjectShouldExistAtEndOfArray()
        {
            var patientResource = new Patient
            {
                Name =
                {
                    new HumanName { Given = new[] { "Chad" }, Family = "Johnson", Use = HumanName.NameUse.Old },
                    new HumanName { Given = new[] { "Chad" }, Family = "Ochocinco", Use = HumanName.NameUse.Old },
                },
            };
            var newName = new HumanName { Given = new[] { "Chad", "Ochocinco" }, Family = "Johnson", Use = HumanName.NameUse.Usual };
            var patchParam = new Parameters().AddAddPatchParameter("Patient", "name", newName);

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource, patchParam).Apply();

            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Name =
                    {
                        new HumanName { Given = new[] { "Chad" }, Family = "Johnson", Use = HumanName.NameUse.Old },
                        new HumanName { Given = new[] { "Chad" }, Family = "Ochocinco", Use = HumanName.NameUse.Old },
                        new HumanName { Given = new[] { "Chad", "Ochocinco" }, Family = "Johnson", Use = HumanName.NameUse.Usual },
                    },
                }));
        }

        [Fact]
        public void GivenAFhirPatchAddRequest_WhenAddingComplexAnonymousType_ThenObjectShouldExistOnResource()
        {
            var patchParam = new Parameters().AddPatchParameter("add", path: "Patient", name: "contact", value: new Parameters.ParameterComponent
                {
                    Name = "name",
                    Value = new HumanName
                    {
                        Given = new List<string> { "a" },
                        Family = "name",
                        Text = "a name",
                        Period = new Period { End = "2020-01-01" },
                    },
                });

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(new Patient(), patchParam).Apply();

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
                                Period = new Period { End = "2020-01-01" },
                            },
                        },
                    },
                }));
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L450
        [Fact]
        public void GivenAFhirPatchAddRequest_WhenAddingToUninitializedObject_ThenListShouldBeCreatedWithObject()
        {
            var patchParam = new Parameters().AddAddPatchParameter("Patient", "identifier", new Identifier() { System = "http://example.org", Value = "value 3" });

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(new Patient(), patchParam).Apply();

            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Identifier = new List<Identifier>()
                    {
                        new Identifier() { System = "http://example.org", Value = "value 3" },
                    },
                }));
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L450
        [Fact]
        public void GivenAFhirPatchAddRequest_WhenAddingToUninitializedObject_ThenInvalidOperationExceptionIsThrown()
        {
            var patchParam = new Parameters().AddAddPatchParameter("Patient.identifier.where(use = 'official').period", "end", new FhirDateTime("2021-07-05"));
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>()
                    {
                        new Identifier() { Use = Identifier.IdentifierUse.Official, Value = "value 3" },
                    },
            };

            Assert.Throws<InvalidOperationException>(new FhirPathPatchBuilder(patientResource, patchParam).Apply);
        }
    }
}
