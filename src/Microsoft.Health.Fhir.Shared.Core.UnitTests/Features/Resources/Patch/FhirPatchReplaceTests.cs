// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Resources.Patch
{
    public class FhirPatchReplaceTests
    {
        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L19
        [Fact]
        public void GivenAFhirPatchReplaceRequest_WhenReplacingPrimitiveVaue_ThenPrimitiveShouldBeChanged()
        {
            var patchParam = new Parameters().AddReplacePatchParameter("Patient.birthDate", new Date("1930-01-01"));
            var patientResource = new Patient { BirthDate = "1920-01-01" };

            var patchedPatientResource = new FhirPathPatchBuilder(patientResource, patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
            {
                BirthDate = "1930-01-01",
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L140
        [Fact]
        public void GivenAFhirPatchReplaceRequest_WhenReplacingPrimitiveInList_ThenPrimitiveShouldBeChanged()
        {
            var patchParam = new Parameters().AddReplacePatchParameter("Patient.contact[0].gender", new Code("female"));
            var patientResource = new Patient
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

            var patchedPatientResource = new FhirPathPatchBuilder(patientResource, patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
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
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L183
        [Fact]
        public void GivenAFhirPatchReplaceRequest_WhenReplacingNestedPrimitiveInList_ThenPrimitiveShouldBeChanged()
        {
            var patchParam = new Parameters().AddReplacePatchParameter("Patient.contact[0].name.text", new FhirString("the name"));
            var patientResource = new Patient
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

            var patchedPatientResource = new FhirPathPatchBuilder(patientResource, patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
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
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L382
        [Fact]
        public void GivenAFhirPatchReplaceRequest_WhenReplacingComplexObject_ThenObjectShouldBeChanged()
        {
            var patchParam = new Parameters().AddReplacePatchParameter("maritalStatus", new CodeableConcept { ElementId = "2", Text = "not married" });
            var patientResource = new Patient
            {
                MaritalStatus = new CodeableConcept
                {
                    ElementId = "1",
                    Text = "married",
                },
            };

            var patchedPatientResource = new FhirPathPatchBuilder(patientResource, patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
            {
                MaritalStatus = new CodeableConcept
                {
                    ElementId = "2",
                    Text = "not married",
                },
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L558
        [Fact]
        public void GivenAFhirPatchReplaceRequest_WhenReplacingListContents_ThenListShouldBeChanged()
        {
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

            var patchedPatientResource = new FhirPathPatchBuilder(patientResource, patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
            {
                Identifier = new List<Identifier>
                {
                    new Identifier { ElementId = "a", System = "http://example.org", Value = "value 2" },
                    new Identifier { ElementId = "b", System = "http://example.org", Value = "value 1" },
                },
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        // Not an official test case, but useful for testing resolution of fhirpath where with replace
        [Fact]
        public void GivenAFhirPatchReplaceRequest_WhenReplacingListContentsUsingWhere_ThenListShouldBeChanged()
        {
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

            var patchedPatientResource = new FhirPathPatchBuilder(patientResource, patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
            {
                Identifier = new List<Identifier>
                {
                    new Identifier { ElementId = "a", System = "http://example.org", Value = "value 2" },
                    new Identifier { ElementId = "b", System = "http://example.org", Value = "value 1" },
                },
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        // Not an official test case, but useful to test exception handling for replace
        [Fact]
        public void GivenAFhirPatchReplaceRequest_WhenGivenInvalidPath_ThenInvalidOperationExceptionIsThrown()
        {
            var patchParam = new Parameters().AddReplacePatchParameter("Patient.none", new FhirString("nothing"));

            var patchOperation = new FhirPathPatchBuilder(new Patient(), patchParam);
            var exception = Assert.Throws<InvalidOperationException>(patchOperation.Apply);
            Assert.Equal("No content found at Patient.none when processing patch replace operation.", exception.Message);
        }

        // Not an official test case, but path for replace operations must return a single element
        [Fact]
        public void GivenAFhirPatchReplaceRequest_WhenReplaceWithMultipleResults_ThenInvalidOperationExceptionIsThrown()
        {
            var patchParam = new Parameters().AddReplacePatchParameter("Patient.identifier.period.start", new FhirDateTime("2021-07-05"));
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>()
                    {
                        new Identifier() { Use = Identifier.IdentifierUse.Official, Value = "value 3", Period = new Period() { Start = "2021-01-01"} },
                        new Identifier() { Use = Identifier.IdentifierUse.Secondary, Value = "value 2", Period = new Period() { Start = "2020-01-01"} },
                    },
            };

            var exception = Assert.Throws<InvalidOperationException>(new FhirPathPatchBuilder(patientResource, patchParam).Apply);
            Assert.Equal("Multiple matches found for Patient.identifier.period.start when processing patch replace operation.", exception.Message);
        }
    }
}
