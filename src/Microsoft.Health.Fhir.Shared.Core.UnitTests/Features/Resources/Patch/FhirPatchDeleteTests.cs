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
        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L51
        [Fact]
        public void GivenAFhirPatchDeleteRequest_WhenDeletingPrimitive_ThenPrimitiveShouldBeRemoved()
        {
            var patchParam = new Parameters().AddDeletePatchParameter("Patient.birthDate");

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(new Patient { BirthDate = "1920-01-01" }).Add(patchParam.Parameter[0]).Apply();

            Assert.True(patchedPatientResource.Matches(new Patient()));
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L225
        [Fact]
        public void GivenAFhirPatchDeleteRequest_WhenDeletingNestedPrimitive_ThenNestedPrimitiveShouldBeRemoved()
        {
            var patchParam = new Parameters().AddDeletePatchParameter("Patient.contact[0].gender");

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

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L262
        [Fact]
        public void GivenAFhirPatchDeleteRequest_WhenDeletingDeepNestedPrimitive_ThenNestedPrimitiveShouldBeRemoved()
        {
            var patchParam = new Parameters().AddDeletePatchParameter("Patient.contact[0].name.text");

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

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L420
        [Fact]
        public void GivenAFhirPatchDeleteRequest_WhenDeletingComplexObject_ThenComplexShouldBeRemoved()
        {
            var patchParam = new Parameters().AddDeletePatchParameter("Patient.maritalStatus");

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(
                new Patient
                {
                    MaritalStatus = new CodeableConcept { Text = "married" },
                }).Add(patchParam.Parameter[0]).Apply();

            Assert.True(patchedPatientResource.Matches(new Patient()));
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L494
        [Fact]
        public void GivenAFhirPatchDeleteRequest_WhenDeletingCDeleteAnonymousObject_ThenAnonymousShouldBeRemoved()
        {
            var patchParam = new Parameters().AddDeletePatchParameter("Patient.contact[0]");

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

            Assert.True(patchedPatientResource.Matches(new Patient()));
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L790
        [Fact]
        public void GivenAFhirPatchDeleteRequest_WhenDeletingFirstFromList_ThenListShouldOnlyContain23()
        {
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

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Add(patchParam.Parameter[0]).Apply();

            Assert.True(patchedPatientResource.Matches(new Patient
            {
                Identifier = new List<Identifier>
                {
                    new Identifier { System = "http://example.org", Value = "value 2" },
                    new Identifier { System = "http://example.org", Value = "value 3" },
                },
            }));
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L836
        [Fact]
        public void GivenAFhirPatchDeleteRequest_WhenDeletingMiddleFromList_ThenListShouldOnlyContain13()
        {
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

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Add(patchParam.Parameter[0]).Apply();

            Assert.True(patchedPatientResource.Matches(new Patient
            {
                Identifier = new List<Identifier>
                {
                    new Identifier { System = "http://example.org", Value = "value 1" },
                    new Identifier { System = "http://example.org", Value = "value 3" },
                },
            }));
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L882
        [Fact]
        public void GivenAFhirPatchDeleteRequest_WhenDeletingEndFromList_ThenListShouldOnlyContain12()
        {
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

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Add(patchParam.Parameter[0]).Apply();

            Assert.True(patchedPatientResource.Matches(new Patient
            {
                Identifier = new List<Identifier>
                {
                    new Identifier { System = "http://example.org", Value = "value 1" },
                    new Identifier { System = "http://example.org", Value = "value 2" },
                },
            }));
        }

        [Fact]
        public void GivenAFhirPatchDeleteRequest_WhenInvalidPathDoesntResolve_OriginalShouldBeReturned()
        {
            var patchParam = new Parameters().AddDeletePatchParameter("Patient.nothing");
            var patchPatient = new Patient
            {
                BirthDate = "1920-01-01",
            };

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patchPatient).Add(patchParam.Parameter[0]).Apply();

            Assert.True(patchedPatientResource.Matches(patchPatient));
        }

        [Fact]
        public void GivenAFhirPatchDeleteRequest_WhenNotPopulatedPathDoesntResolve_OriginalShouldBeReturned()
        {
            var patchParam = new Parameters().AddDeletePatchParameter("Contact.name");
            var patchPatient = new Patient
            {
                BirthDate = "1989-07-05",
            };

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patchPatient).Add(patchParam.Parameter[0]).Apply();

            Assert.True(patchedPatientResource.Matches(patchPatient));
        }
    }
}
