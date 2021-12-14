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
    public class FhirPatchInsertTests
    {
        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L619
        [Fact]
        public void GivenAFhirPatchInsertRequest_WhenInsertingEndOfList_ThenValueShouldExistAtEndOfList()
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

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Add(patchParam.Parameter[0]).Apply();

            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                    },
                }));
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

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Add(patchParam.Parameter[0]).Apply();

            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                    },
                }));
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

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Add(patchParam.Parameter[0]).Apply();

            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 3" },
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                    },
                }));
        }

        [Fact]
        public void GivenAFhirPatchInsertRequest_WhenInsertingInvalidPath_ThenInvalidOperationExceptionShouldBeThrown()
        {
            var patchParam = new Parameters().AddInsertPatchParameter("Patient.nothing", new FhirString("test"), 2);

            var builder = new FhirPathPatchBuilder(new Patient()).Build(patchParam);
            Assert.Throws<InvalidOperationException>(builder.Apply);
        }

        // Not a defined test case, but the path will not resolve and thus a exception is expected.
        // The FHIRPath must return a single element according to the spec and the path does not resolve.
        [Fact]
        public void GivenAFhirPatchInsertRequest_WhenInsertingToUninitializedList_ThenInvalidOperationExceptionShouldBeThrown()
        {
            var patchParam = new Parameters().AddInsertPatchParameter("Patient.identifier", new Identifier { System = "http://example.org", Value = "value 3" }, 2);

            var builder = new FhirPathPatchBuilder(new Patient()).Build(patchParam);
            Assert.Throws<InvalidOperationException>(builder.Apply);
        }

        [Fact]
        public void GivenAFhirPatchInsertRequest_WhenInsertingUninitializedList_ThenInvalidOperationExceptionShouldBeThrown()
        {
            var patchParam = new Parameters().AddInsertPatchParameter("Patient.identifier", new Identifier { System = "http://example.org", Value = "value 3" }, 2);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                    },
            };

            var builder = new FhirPathPatchBuilder(patientResource).Build(patchParam);
            Assert.Throws<InvalidOperationException>(builder.Apply);
        }

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

            var builder = new FhirPathPatchBuilder(patientResource).Build(patchParam);
            Assert.Throws<InvalidOperationException>(builder.Apply);
        }
    }
}
