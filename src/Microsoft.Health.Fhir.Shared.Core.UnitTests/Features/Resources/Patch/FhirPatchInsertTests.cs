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
    public class FhirPatchInsertTests
    {
        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L619
        [Fact]
        public void GivenAFhirPatchInsertRequest_WhenInsertingEndOfList_ThenValueShouldExistAfotEndOfList()
        {
            var patchParam = new Parameters().AddInsertPatchParameter("Patient.identifier", new Identifier { System = "http://example.org", Value = "value 3" }, 2);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                    },
            };

            var patchedPatientResource = new FhirPathPatchBuilder(patientResource, patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
            {
                Identifier = new List<Identifier>
                {
                    new Identifier { System = "http://example.org", Value = "value 1" },
                    new Identifier { System = "http://example.org", Value = "value 2" },
                    new Identifier { System = "http://example.org", Value = "value 3" },
                },
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L676
        [Fact]
        public void GivenAFhirPatchInsertRequest_WhenInsertingMiddleOfList_ThenValueShouldExistAtMiddleOfList()
        {
            var patchParam = new Parameters().AddInsertPatchParameter("Patient.identifier", new Identifier { System = "http://example.org", Value = "value 3" }, 1);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                    },
            };

            var patchedPatientResource = new FhirPathPatchBuilder(patientResource, patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
            {
                Identifier = new List<Identifier>
                {
                    new Identifier { System = "http://example.org", Value = "value 1" },
                    new Identifier { System = "http://example.org", Value = "value 3" },
                    new Identifier { System = "http://example.org", Value = "value 2" },
                },
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L733
        [Fact]
        public void GivenAFhirPatchInsertRequest_WhenInsertingStartOfList_ThenValueShouldExistAtStartOfList()
        {
            var patchParam = new Parameters().AddInsertPatchParameter("Patient.identifier", new Identifier { System = "http://example.org", Value = "value 3" }, 0);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                    },
            };

            var patchedPatientResource = new FhirPathPatchBuilder(patientResource, patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
            {
                Identifier = new List<Identifier>
                {
                    new Identifier { System = "http://example.org", Value = "value 3" },
                    new Identifier { System = "http://example.org", Value = "value 1" },
                    new Identifier { System = "http://example.org", Value = "value 2" },
                },
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        [Fact]
        public void GivenAFhirPatchInsertRequest_WhenInsertingInvalidPath_ThenInvalidOperationExceptionShouldBeThrown()
        {
            var patchParam = new Parameters().AddInsertPatchParameter("Patient.nothing", new FhirString("test"), 2);

            var builder = new FhirPathPatchBuilder(new Patient(), patchParam);
            var exception = Assert.Throws<InvalidOperationException>(builder.Apply);
            Assert.Contains("No content found at Patient.nothing", exception.Message);
        }

        // Not a defined test case, but the path will not resolve and thus a exception is expected.
        // The FHIRPath must return a single element according to the spec and the path does not resolve.
        [Fact]
        public void GivenAFhirPatchInsertRequest_WhenInsertingToUninitializedList_ThenInvalidOperationExceptionShouldBeThrown()
        {
            var patchParam = new Parameters().AddInsertPatchParameter("Patient.identifier", new Identifier { System = "http://example.org", Value = "new value" }, 0);

            var builder = new FhirPathPatchBuilder(new Patient(), patchParam);
            var exception = Assert.Throws<InvalidOperationException>(builder.Apply);
            Assert.Contains("No content found at Patient.identifier", exception.Message);
        }

        // Not a defined test case, but the path will not resolve and thus a exception is expected.
        // The FHIRPath must return a single element or list according to the spec and the path does not resolve.
        [Fact]
        public void GivenAFhirPatchInsertRequest_WhenPathHasMultipleMatches_ThenInvalidOperationExceptionShouldBeThrown()
        {
            var patchParam = new Parameters().AddInsertPatchParameter("Patient.contact.name.given", new FhirString { Value = "Fake"}, 0);
            var patientResource = new Patient
            {
                Contact = new List<Patient.ContactComponent>
                    {
                        new Patient.ContactComponent { Name = new HumanName() { Family = "Smith", Given = new List<string>() { "Bob" } } },
                        new Patient.ContactComponent { Name = new HumanName() { Family = "Smith", Given = new List<string>() { "Jane" } } },
                    },
            };

            var builder = new FhirPathPatchBuilder(patientResource, patchParam);
            var exception = Assert.Throws<InvalidOperationException>(builder.Apply);
            Assert.Contains("Multiple matches found at Patient.contact.name.given", exception.Message);
        }

        // Not an official test case, but testing index out of range.
        [Fact]
        public void GivenAFhirPatchInsertRequest_WhenInsertingWithOutOfRangeIndex_ThenInvalidOperationExceptionShouldBeThrown()
        {
            var patchParam = new Parameters().AddInsertPatchParameter("Patient.identifier", new Identifier { System = "http://example.org", Value = "value 3" }, 2);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                    },
            };

            var builder = new FhirPathPatchBuilder(patientResource, patchParam);
            var exception = Assert.Throws<InvalidOperationException>(builder.Apply);
            Assert.Contains("Index 2 out of bounds", exception.Message);
        }

        // Not an official test case, but testing index out of range.
        [Fact]
        public void GivenAFhirPatchInsertRequest_WhenInsertingWithNegativeIndex_ThenInvalidOperationExceptionShouldBeThrown()
        {
            var patchParam = new Parameters().AddInsertPatchParameter("Patient.identifier", new Identifier { System = "http://example.org", Value = "value 3" }, -1);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                    },
            };

            var builder = new FhirPathPatchBuilder(patientResource, patchParam);
            var exception = Assert.Throws<InvalidOperationException>(builder.Apply);
            Assert.Contains("Index -1 out of bounds", exception.Message);
        }
    }
}
