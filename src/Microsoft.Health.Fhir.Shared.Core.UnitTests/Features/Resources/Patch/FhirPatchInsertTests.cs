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
        // HL7 published tests

        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L619
        /// </summary>
        [Fact]
        public void AddToList()
        {
            // Arrange
            var patchParam = new Parameters().AddInsertPatchParameter("Patient.identifier", new Identifier { System = "http://example.org", Value = "value 3" }, 2);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                    },
            };

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Add(patchParam.Parameter[0]).Apply();

            // Assert
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

        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L676
        /// </summary>
        [Fact]
        public void InsertToList1()
        {
            // Arrange
            var patchParam = new Parameters().AddInsertPatchParameter("Patient.identifier", new Identifier { System = "http://example.org", Value = "value 3" }, 1);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                    },
            };

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Add(patchParam.Parameter[0]).Apply();

            // Assert
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

        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L733
        /// </summary>
        [Fact]
        public void InsertToList2()
        {
            // Arrange
            var patchParam = new Parameters().AddInsertPatchParameter("Patient.identifier", new Identifier { System = "http://example.org", Value = "value 3" }, 0);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                    },
            };

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Add(patchParam.Parameter[0]).Apply();

            // Assert
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

        /// <summary>
        /// Insert with a path that doesn't resolve should throw an exception
        /// </summary>
        [Fact]
        public void InsertInvalidPath()
        {
            // Arrange
            var patchParam = new Parameters().AddInsertPatchParameter("Patient.nothing", new FhirString("test"), 2);

            // Act / Assert
            var builder = new FhirPathPatchBuilder(new Patient()).Build(patchParam);
            Assert.Throws<InvalidOperationException>(builder.Apply);
        }

        /* Commented out until proper behavior is found.
        /// <summary>
        /// Insert into a list that hasnt been initialized on the resource
        /// </summary>
        [Fact]
        public void InsertToListThatDoesntExist()
        {
            // Arrange
            var patchParam = new Parameters().AddInsertPatchParameter("Patient.identifier", new Identifier { System = "http://example.org", Value = "value 3" }, 2);
            var patientResource = new Patient();

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Build(patchParam).Apply();

            // Assert
            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 3" },
                    }
                }
            ));
        } */

        /// <summary>
        /// Tests insert with index > list length throws exception
        /// </summary>
        [Fact]
        public void InsertInvalidIndex1()
        {
            // Arrange
            var patchParam = new Parameters().AddInsertPatchParameter("Patient.identifier", new Identifier { System = "http://example.org", Value = "value 3" }, 2);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                    },
            };

            // Act / Assert
            var builder = new FhirPathPatchBuilder(patientResource).Build(patchParam);
            Assert.Throws<InvalidOperationException>(builder.Apply);
        }

        /// <summary>
        /// Tests insert with index < list length throws exception
        /// </summary>
        [Fact]
        public void InsertInvalidIndex2()
        {
            // Arrange
            var patchParam = new Parameters().AddInsertPatchParameter("Patient.identifier", new Identifier { System = "http://example.org", Value = "value 3" }, -1);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                    },
            };

            // Act / Assert
            var builder = new FhirPathPatchBuilder(patientResource).Build(patchParam);
            Assert.Throws<InvalidOperationException>(builder.Apply);
        }

        /* Not sure how this should be handled.
        /// <summary>
        /// Tests insert with non-matching data types
        /// </summary>

       [Fact]
        public void InsertNonMatchingTypes()
        {
            // Arrange
            var patchParam = new Parameters().AddInsertPatchParameter("Patient.identifier", new FhirDecimal(-42), 0);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" }
                    }
            };

            // Act / Assert
            var builder = new FhirPathPatchBuilder(patientResource).Build(patchParam);
            Assert.Throws<InvalidOperationException>(builder.Apply);
        } */
    }
}
