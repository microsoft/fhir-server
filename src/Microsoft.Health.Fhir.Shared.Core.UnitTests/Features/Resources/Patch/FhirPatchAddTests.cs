// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch.Helpers;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Resources.Patch
{
    public class FhirPatchAddTests
    {
        private FhirJsonSerializer testSerializer = new FhirJsonSerializer();

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L78
        [Fact]
        public void GivenAFhirPatchAddRequest_WhenAddingPrimitiveVaue_ThenPrimitiveShouldBePopulated()
        {
            var patchParam = new Parameters().AddAddPatchParameter("Patient", "birthDate", new Date("1930-01-01"));

            var patchedPatientResource = new FhirPathPatchBuilder(new Patient(), patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
            {
                BirthDate = "1930-01-01",
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
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

            var patchedPatientResource = new FhirPathPatchBuilder(patientResource, patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
            {
                Contact = new List<Patient.ContactComponent>
                    {
                        new Patient.ContactComponent
                        {
                            Name = new HumanName { Text = "a name" },
                            Gender = AdministrativeGender.Male,
                        },
                    },
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L343
        [Fact]
        public void GivenAFhirPatchAddRequest_WhenAddingComplexValue_ThenComplexShouldBePopulated()
        {
            var patchParam = new Parameters().AddAddPatchParameter("Patient", "maritalStatus", new CodeableConcept { Text = "married" });
            var patchedPatientResource = new FhirPathPatchBuilder(new Patient(), patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
            {
                MaritalStatus = new CodeableConcept { Text = "married" },
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L450
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

            var patchedPatientResource = new FhirPathPatchBuilder(new Patient(), patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
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
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        // Not in official test cases - this tests the use of `where` and populating an element inside the result.
        [Fact]
        public void GivenAFhirPatchAddRequest_WhenAddingDeepPrimitiveUsingWhere_ThenNestedPrimitiveShouldBePopulatedOnCorrectElement()
        {
            var patchParam = new Parameters().AddAddPatchParameter("Patient.identifier.where(use = 'official')", "period", new Period { EndElement = new FhirDateTime("2021-12-01") });
            var patientResource = new Patient
            {
                Identifier = { new Identifier() { Use = Identifier.IdentifierUse.Official, Value = "123" } },
            };

            var patchedPatientResource = new FhirPathPatchBuilder(patientResource, patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
            {
                Identifier = { new Identifier() { Use = Identifier.IdentifierUse.Official, Value = "123", Period = new Period { EndElement = new FhirDateTime("2021-12-01") } } },
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        // Not in official test cases - this ensures that an add on a list means the element will be added on the end of the list.
        [Fact]
        public void GivenAFhirPatchAddRequest_WhenAddingToList_ThenValueShouldExistAtEndOfList()
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

            var patchedPatientResource = new FhirPathPatchBuilder(patientResource, patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
            {
                Name =
                {
                    new HumanName { Given = new[] { "Chad" }, Family = "Johnson", Use = HumanName.NameUse.Old },
                    new HumanName { Given = new[] { "Chad" }, Family = "Ochocinco", Use = HumanName.NameUse.Old },
                    new HumanName { Given = new[] { "Chad", "Ochocinco" }, Family = "Johnson", Use = HumanName.NameUse.Usual },
                },
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/master/r4/patch/fhir-path-tests.xml#L1416
        [Fact]
        public void GivenAFhirPatchAddRequest_WhenAddingToUninitializedList_ThenListShouldBeCreatedWithObject()
        {
            var patchParam = new Parameters().AddAddPatchParameter("Patient", "identifier", new Identifier() { System = "http://example.org", Value = "value 3" });

            var patchedPatientResource = new FhirPathPatchBuilder(new Patient(), patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
            {
                Identifier = new List<Identifier>()
                {
                    new Identifier() { System = "http://example.org", Value = "value 3" },
                },
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }
    }
}
