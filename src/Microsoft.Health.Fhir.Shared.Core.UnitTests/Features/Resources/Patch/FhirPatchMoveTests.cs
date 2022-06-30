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
    public class FhirPatchMoveTests
    {
        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L928
        [Fact]
        public void GivenAFhirPatchMoveRequest_WhenMoving3To1_TheListShoudBe1423()
        {
            var patchParam = new Parameters().AddMovePatchParameter("Patient.identifier", 3, 1);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                        new Identifier { System = "http://example.org", Value = "value 4" },
                    },
            };

            var patchedPatientResource = new FhirPathPatchBuilder(patientResource, patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
            {
                Identifier = new List<Identifier>
                {
                    new Identifier { System = "http://example.org", Value = "value 1" },
                    new Identifier { System = "http://example.org", Value = "value 4" },
                    new Identifier { System = "http://example.org", Value = "value 2" },
                    new Identifier { System = "http://example.org", Value = "value 3" },
                },
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L994
        [Fact]
        public void GivenAFhirPatchMoveRequest_WhenMoving3To0_TheListShoudBe4123()
        {
            var patchParam = new Parameters().AddMovePatchParameter("Patient.identifier", 3, 0);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                        new Identifier { System = "http://example.org", Value = "value 4" },
                    },
            };

            var patchedPatientResource = new FhirPathPatchBuilder(patientResource, patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
            {
                Identifier = new List<Identifier>
                {
                    new Identifier { System = "http://example.org", Value = "value 4" },
                    new Identifier { System = "http://example.org", Value = "value 1" },
                    new Identifier { System = "http://example.org", Value = "value 2" },
                    new Identifier { System = "http://example.org", Value = "value 3" },
                },
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L1060
        [Fact]
        public void GivenAFhirPatchMoveRequest_WhenMoving3To2_TheListShoudBe1243()
        {
            var patchParam = new Parameters().AddMovePatchParameter("Patient.identifier", 3, 2);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                        new Identifier { System = "http://example.org", Value = "value 4" },
                    },
            };

            var patchedPatientResource = new FhirPathPatchBuilder(patientResource, patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
            {
                Identifier = new List<Identifier>
                {
                    new Identifier { System = "http://example.org", Value = "value 1" },
                    new Identifier { System = "http://example.org", Value = "value 2" },
                    new Identifier { System = "http://example.org", Value = "value 4" },
                    new Identifier { System = "http://example.org", Value = "value 3" },
                },
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L1126
        [Fact]
        public void GivenAFhirPatchMoveRequest_WhenMoving0To3_TheListShoudBe2341()
        {
            var patchParam = new Parameters().AddMovePatchParameter("Patient.identifier", 0, 3);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                        new Identifier { System = "http://example.org", Value = "value 4" },
                    },
            };

            var patchedPatientResource = new FhirPathPatchBuilder(patientResource, patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
            {
                Identifier = new List<Identifier>
                {
                    new Identifier { System = "http://example.org", Value = "value 2" },
                    new Identifier { System = "http://example.org", Value = "value 3" },
                    new Identifier { System = "http://example.org", Value = "value 4" },
                    new Identifier { System = "http://example.org", Value = "value 1" },
                },
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L1192
        [Fact]
        public void GivenAFhirPatchMoveRequest_WhenMoving1To0Then2To1_TheListShoudBe2314()
        {
            var patchParam = new Parameters()
                .AddMovePatchParameter("Patient.identifier", 1, 0)
                .AddMovePatchParameter("Patient.identifier", 2, 1);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                        new Identifier { System = "http://example.org", Value = "value 4" },
                    },
            };

            var patchedPatientResource = new FhirPathPatchBuilder(patientResource, patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
            {
                Identifier = new List<Identifier>
                {
                    new Identifier { System = "http://example.org", Value = "value 2" },
                    new Identifier { System = "http://example.org", Value = "value 3" },
                    new Identifier { System = "http://example.org", Value = "value 1" },
                    new Identifier { System = "http://example.org", Value = "value 4" },
                },
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L1277
        [Fact]
        public void GivenAFhirPatchMoveRequest_WhenMoving3To0Then3To1Then3To2_TheListShoudBe4321()
        {
            var patchParam = new Parameters()
                .AddMovePatchParameter("Patient.identifier", 3, 0)
                .AddMovePatchParameter("Patient.identifier", 3, 1)
                .AddMovePatchParameter("Patient.identifier", 3, 2);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                        new Identifier { System = "http://example.org", Value = "value 4" },
                    },
            };

            var patchedPatientResource = new FhirPathPatchBuilder(patientResource, patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
            {
                Identifier = new List<Identifier>
                {
                    new Identifier { System = "http://example.org", Value = "value 4" },
                    new Identifier { System = "http://example.org", Value = "value 3" },
                    new Identifier { System = "http://example.org", Value = "value 2" },
                    new Identifier { System = "http://example.org", Value = "value 1" },
                },
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        [Fact]
        public void GivenAFhirPatchMoveRequest_WhenMovingInvalidPath_ThenInvalidOperationExceptionShouldBeThrown()
        {
            var patchParam = new Parameters().AddMovePatchParameter("Patient.nothing", 0, 1);

            var builder = new FhirPathPatchBuilder(new Patient(), patchParam);
            var exception = Assert.Throws<InvalidOperationException>(builder.Apply);
            Assert.Equal("No content found at Patient.nothing when processing patch move operation.", exception.Message);
        }

        // Not a defined test case, but the path will not resolve and thus a exception is expected.
        // The FHIRPath must return a single element according to the spec and the path does not resolve.
        [Fact]
        public void GivenAFhirPatchMoveRequest_WhenMovingOnUninitializedList_ThenInvalidOperationExceptionShouldBeThrown()
        {
            var patchParam = new Parameters().AddMovePatchParameter("Patient.identifier", 0, 1);

            var builder = new FhirPathPatchBuilder(new Patient(), patchParam);
            var exception = Assert.Throws<InvalidOperationException>(builder.Apply);
            Assert.Equal("No content found at Patient.identifier when processing patch move operation.", exception.Message);
        }

        // Not a defined test case, but the path will not resolve and thus a exception is expected.
        // The FHIRPath must return a single element or list according to the spec and the path does not resolve.
        [Fact]
        public void GivenAFhirPatchMoveRequest_WhenPathHasMultipleMatches_ThenInvalidOperationExceptionShouldBeThrown()
        {
            var patchParam = new Parameters().AddMovePatchParameter("Patient.contact.name.given", 0, 1);
            var patientResource = new Patient
            {
                Contact = new List<Patient.ContactComponent>
                    {
                        new Patient.ContactComponent { Name = new HumanName() { Family = "Smith", Given = new List<string>() { "Bob", "Middle" } } },
                        new Patient.ContactComponent { Name = new HumanName() { Family = "Smith", Given = new List<string>() { "Jane", "Middle" } } },
                    },
            };

            var builder = new FhirPathPatchBuilder(patientResource, patchParam);
            var exception = Assert.Throws<InvalidOperationException>(builder.Apply);
            Assert.Equal("Multiple matches found for Patient.contact.name.given when processing patch move operation.", exception.Message);
        }

        // Not an official test case, but ensuring proper error messages for out of index errors.
        [Fact]
        public void GivenAFhirPatchMoveRequest_WhenMovingFromInvalidIndex_ThenInvalidOperationExceptionIsThrown()
        {
            var patchParam = new Parameters().AddMovePatchParameter("Patient.identifier", 3, 0);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                    },
            };

            var builder = new FhirPathPatchBuilder(patientResource, patchParam);
            var exception = Assert.Throws<InvalidOperationException>(builder.Apply);
            Assert.Equal("Source 3 out of bounds when processing patch move operation.", exception.Message);
        }

        // Not an official test case, but ensuring proper error messages for out of index errors.
        [Fact]
        public void GivenAFhirPatchMoveRequest_WhenMovingFromNegativeIndex_ThenInvalidOperationExceptionIsThrown()
        {
            var patchParam = new Parameters().AddMovePatchParameter("Patient.identifier", -1, 0);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                    },
            };

            var builder = new FhirPathPatchBuilder(patientResource, patchParam);
            var exception = Assert.Throws<InvalidOperationException>(builder.Apply);
            Assert.Equal("Source -1 out of bounds when processing patch move operation.", exception.Message);
        }

        // Not an official test case, but ensuring proper error messages for out of index errors.
        [Fact]
        public void GivenAFhirPatchMoveRequest_WhenMovingToInvalidIndex_ThenInvalidOperationExceptionIsThrown()
        {
            var patchParam = new Parameters().AddMovePatchParameter("Patient.identifier", 0, 3);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                    },
            };

            var builder = new FhirPathPatchBuilder(patientResource, patchParam);
            var exception = Assert.Throws<InvalidOperationException>(builder.Apply);
            Assert.Equal("Destination 3 out of bounds when processing patch move operation.", exception.Message);
        }

        // Not an official test case, but ensuring proper error messages for out of index errors.
        [Fact]
        public void GivenAFhirPatchMoveRequest_WhenMovingToNegativeIndex_ThenInvalidOperationExceptionIsThrown()
        {
            var patchParam = new Parameters().AddMovePatchParameter("Patient.identifier", 0, -1);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                    },
            };

            var builder = new FhirPathPatchBuilder(patientResource, patchParam);
            var exception = Assert.Throws<InvalidOperationException>(builder.Apply);
            Assert.Equal("Destination -1 out of bounds when processing patch move operation.", exception.Message);
        }
    }
}
