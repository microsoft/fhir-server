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
        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L19
        [Fact]
        public void GivenAFhirPatchReplaceRequest_WhenReplacingPrimitiveVaue_ThenPrimitiveShouldBeChanged()
        {
            var patchParam = new Parameters().AddReplacePatchParameter("Patient.birthDate", new Date("1930-01-01"));
            var origPatient = new Patient { BirthDate = "1920-01-01" };

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(origPatient, patchParam).Apply();

            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    BirthDate = "1930-01-01",
                }));
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L140
        [Fact]
        public void GivenAFhirPatchReplaceRequest_WhenReplacingPrimitiveInList_ThenPrimitiveShouldBeChanged()
        {
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

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(origPatient, patchParam).Apply();

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

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L183
        [Fact]
        public void GivenAFhirPatchReplaceRequest_WhenReplacingNestedPrimitiveInList_ThenPrimitiveShouldBeChanged()
        {
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

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(origPatient, patchParam).Apply();

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

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L382
        [Fact]
        public void GivenAFhirPatchReplaceRequest_WhenReplacingComplexObject_ThenObjectShouldBeChanged()
        {
            var patchParam = new Parameters().AddReplacePatchParameter("maritalStatus", new CodeableConcept { ElementId = "2", Text = "not married" });
            var origPatient = new Patient
            {
                MaritalStatus = new CodeableConcept
                {
                    ElementId = "1",
                    Text = "married",
                },
            };

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(origPatient, patchParam).Apply();

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

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource, patchParam).Apply();

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

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource, patchParam).Apply();

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

        [Fact]
        public void GivenAFhirPatchReplaceRequest_WhenGivenInvalidPath_ThenInvalidOperationExceptionIsThrown()
        {
            var patchParam = new Parameters().AddReplacePatchParameter("Patient.none", new FhirString("nothing"));

            var patchOperation = new FhirPathPatchBuilder(new Patient(), patchParam);
            Assert.Throws<InvalidOperationException>(patchOperation.Apply);
        }
    }
}
