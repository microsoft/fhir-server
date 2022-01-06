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
    public class FhirPatchMoveTests
    {
        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L928
        [Fact]
        public void GivenAFhirPatchMoveequest_WhenMoving3To1_TheListShoudBe1423()
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

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource, patchParam).Apply();

            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 4" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                    },
                }));
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L994
        [Fact]
        public void GivenAFhirPatchMoveequest_WhenMoving3To0_TheListShoudBe4123()
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

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource, patchParam).Apply();

            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 4" },
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                    },
                }));
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L1060
        [Fact]
        public void GivenAFhirPatchMoveequest_WhenMoving3To2_TheListShoudBe1243()
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

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource, patchParam).Apply();

            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 4" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                    },
                }));
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L1126
        [Fact]
        public void GivenAFhirPatchMoveequest_WhenMoving0To2_TheListShoudBe2341()
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

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource, patchParam).Apply();

            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                        new Identifier { System = "http://example.org", Value = "value 4" },
                        new Identifier { System = "http://example.org", Value = "value 1" },
                    },
                }));
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L1192
        [Fact]
        public void GivenAFhirPatchMoveequest_WhenMoving1To0Then2To1_TheListShoudBe2314()
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

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource, patchParam).Apply();

            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 4" },
                    },
                }));
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L1277
        [Fact]
        public void GivenAFhirPatchMoveequest_WhenMoving3To0Then3To1Then3Ti2_TheListShoudBe4321()
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

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource, patchParam).Apply();

            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 4" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 1" },
                    },
                }));
        }

        [Fact]
        public void GivenAFhirPatchMoveequest_WhenMovingOnInvalidPath_ThenInvalidOperationExceptionIsThrown()
        {
            var patchParam = new Parameters().AddMovePatchParameter("Patient.nothing", 0, 2);

            var builder = new FhirPathPatchBuilder(new Patient(), patchParam);
            Assert.Throws<InvalidOperationException>(builder.Apply);
        }

        [Fact]
        public void GivenAFhirPatchMoveequest_WhenMovingOnUninitializedPath_ThenInvalidOperationExceptionIsThrown()
        {
            var patchParam = new Parameters().AddMovePatchParameter("Patient.identifier", 0, 0);

            var builder = new FhirPathPatchBuilder(new Patient(), patchParam);
            Assert.Throws<InvalidOperationException>(builder.Apply);
        }

        [Fact]
        public void GivenAFhirPatchMoveequest_WhenMovingFromInvalidIndex_ThenInvalidOperationExceptionIsThrown()
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
            Assert.Throws<InvalidOperationException>(builder.Apply);
        }

        [Fact]
        public void GivenAFhirPatchMoveequest_WhenMovingFromNegativeIndex_ThenInvalidOperationExceptionIsThrown()
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
            Assert.Throws<InvalidOperationException>(builder.Apply);
        }

        [Fact]
        public void GivenAFhirPatchMoveequest_WhenMovingToInvalidIndex_ThenInvalidOperationExceptionIsThrown()
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
            Assert.Throws<InvalidOperationException>(builder.Apply);
        }

        [Fact]
        public void GivenAFhirPatchMoveequest_WhenMovingToNegativeIndex_ThenInvalidOperationExceptionIsThrown()
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
            Assert.Throws<InvalidOperationException>(builder.Apply);
        }
    }
}
