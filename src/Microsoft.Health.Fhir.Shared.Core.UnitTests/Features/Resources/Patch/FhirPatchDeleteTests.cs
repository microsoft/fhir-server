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
    public class FhirPatchDeleteTests
    {
        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L51
        [Fact]
        public void GivenAFhirPatchDeleteRequest_WhenDeletingPrimitive_ThenPrimitiveShouldBeRemoved()
        {
            var patchParam = new Parameters().AddDeletePatchParameter("Patient.birthDate");

            Patient patchedPatientResource = new FhirPathPatchBuilder(new Patient { BirthDate = "1920-01-01" }, patchParam).Apply() as Patient;

            Assert.Equal(patchedPatientResource.ToJson(), new Patient().ToJson());
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L225
        [Fact]
        public void GivenAFhirPatchDeleteRequest_WhenDeletingNestedPrimitive_ThenNestedPrimitiveShouldBeRemoved()
        {
            var patchParam = new Parameters().AddDeletePatchParameter("Patient.contact[0].gender");

            var patchedPatientResource = new FhirPathPatchBuilder(
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
                },
                patchParam).Apply() as Patient;

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
                        },
                    },
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L262
        [Fact]
        public void GivenAFhirPatchDeleteRequest_WhenDeletingDeepNestedPrimitive_ThenDeepNestedPrimitiveShouldBeRemoved()
        {
            var patchParam = new Parameters().AddDeletePatchParameter("Patient.contact[0].name.text");

            var patchedPatientResource = new FhirPathPatchBuilder(
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
                },
                patchParam).Apply() as Patient;

            var expectedPatientResource = new Patient
            {
                Contact = new List<Patient.ContactComponent>
                    {
                        new Patient.ContactComponent
                        {
                            Gender = AdministrativeGender.Male,
                        },
                    },
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L420
        [Fact]
        public void GivenAFhirPatchDeleteRequest_WhenDeletingComplexObject_ThenComplexShouldBeRemoved()
        {
            var patchParam = new Parameters().AddDeletePatchParameter("Patient.maritalStatus");

            var patchedPatientResource = new FhirPathPatchBuilder(
                new Patient
                {
                    MaritalStatus = new CodeableConcept { Text = "married" },
                },
                patchParam).Apply() as Patient;

            Assert.Equal(patchedPatientResource.ToJson(), new Patient().ToJson());
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L494
        [Fact]
        public void GivenAFhirPatchDeleteRequest_WhenDeletingDeleteAnonymousObject_ThenAnonymousShouldBeRemoved()
        {
            var patchParam = new Parameters().AddDeletePatchParameter("Patient.contact[0]");

            var patchedPatientResource = new FhirPathPatchBuilder(
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
                },
                patchParam).Apply() as Patient;

            Assert.Equal(patchedPatientResource.ToJson(), new Patient().ToJson());
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

            var patchedPatientResource = new FhirPathPatchBuilder(patientResource, patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
            {
                Identifier = new List<Identifier>
                {
                    new Identifier { System = "http://example.org", Value = "value 2" },
                    new Identifier { System = "http://example.org", Value = "value 3" },
                },
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
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

            var patchedPatientResource = new FhirPathPatchBuilder(patientResource, patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
            {
                Identifier = new List<Identifier>
                {
                    new Identifier { System = "http://example.org", Value = "value 1" },
                    new Identifier { System = "http://example.org", Value = "value 3" },
                },
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
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

            var patchedPatientResource = new FhirPathPatchBuilder(patientResource, patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
            {
                Identifier = new List<Identifier>
                {
                    new Identifier { System = "http://example.org", Value = "value 1" },
                    new Identifier { System = "http://example.org", Value = "value 2" },
                },
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        // Not an official test case, but delete is only allowed for 1 element
        [Fact]
        public void GivenAFhirPatchDeleteRequest_WhenDeletingMultipleElements_ThenInvalidOperationExceptionShouldBeThrown()
        {
            var patchParam = new Parameters().AddDeletePatchParameter("Patient.identifier");
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                    },
            };

            var exception = Assert.Throws<InvalidOperationException>(new FhirPathPatchBuilder(patientResource, patchParam).Apply);
            Assert.Contains("Multiple matches found", exception.Message);
        }

        // Not an official test case, but any delete operation on a path that doesn't resolve should return orig resource.
        [Fact]
        public void GivenAFhirPatchDeleteRequest_WhenInvalidPathDoesntResolve_OriginalShouldBeReturned()
        {
            var patchParam = new Parameters().AddDeletePatchParameter("Patient.nothing");
            var patientResource = new Patient
            {
                BirthDate = "1920-01-01",
            };

            var patchedPatientResource = new FhirPathPatchBuilder(patientResource, patchParam).Apply() as Patient;

            Assert.Equal(patchedPatientResource.ToJson(), patientResource.ToJson());
        }

        // Not an official test case, but any delete operation on a path that doesn't resolve should return orig resource.
        [Fact]
        public void GivenAFhirPatchDeleteRequest_WhenNotPopulatedPathDoesntResolve_OriginalShouldBeReturned()
        {
            var patchParam = new Parameters().AddDeletePatchParameter("Contact.name");
            var patientResource = new Patient
            {
                BirthDate = "1989-07-05",
            };

            var patchedPatientResource = new FhirPathPatchBuilder(patientResource, patchParam).Apply() as Patient;

            Assert.Equal(patchedPatientResource.ToJson(), patientResource.ToJson());
        }
    }
}
