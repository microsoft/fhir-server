using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources.Patch
{
    public class FhirPatchMoveTests
    {
        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L928
        /// </summary>
        [Fact]
        public void ReorderList1()
        {
            // Arrange
            var patchParam = new Parameters().AddMovePatchParameter("Patient.identifier", 3, 1);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                        new Identifier { System = "http://example.org", Value = "value 4" },
                    }
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
                        new Identifier { System = "http://example.org", Value = "value 4" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                    }
                }
            ));
        }

        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L994
        /// </summary>
        [Fact]
        public void ReorderList2()
        {
            // Arrange
            var patchParam = new Parameters().AddMovePatchParameter("Patient.identifier", 3, 0);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                        new Identifier { System = "http://example.org", Value = "value 4" },
                    }
            };

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 4" },
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                    }
                }
            ));
        }

        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L1060
        /// </summary>
        [Fact]
        public void ReorderList3()
        {
            // Arrange
            var patchParam = new Parameters().AddMovePatchParameter("Patient.identifier", 3, 2);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                        new Identifier { System = "http://example.org", Value = "value 4" },
                    }
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
                        new Identifier { System = "http://example.org", Value = "value 4" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                    }
                }
            ));
        }

        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L1126
        /// </summary>
        [Fact]
        public void ReorderList4()
        {
            // Arrange
            var patchParam = new Parameters().AddMovePatchParameter("Patient.identifier", 0, 3);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                        new Identifier { System = "http://example.org", Value = "value 4" },
                    }
            };

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource).Add(patchParam.Parameter[0]).Apply();

            // Assert
            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                        new Identifier { System = "http://example.org", Value = "value 4" },
                        new Identifier { System = "http://example.org", Value = "value 1" },
                    }
                }
            ));
        }

        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L1192
        /// </summary>
        [Fact]
        public void ReorderList5()
        {
            // Arrange
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
                    }
            };

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource)
                .Add(patchParam.Parameter[0])
                .Add(patchParam.Parameter[1])
                .Apply();

            // Assert
            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 4" },
                    }
                }
            ));
        }

        /// <summary>
        /// Implements test case at:
        /// https://github.com/FHIR/fhir-test-cases/blob/752b01313ecbc1e13a942e1b3e25c96b3f7f3449/r5/patch/fhir-path-tests.xml#L1277
        /// </summary>
        [Fact]
        public void ReorderList6()
        {
            // Arrange
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
                    }
            };

            // Act
            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource)
                .Add(patchParam.Parameter[0])
                .Add(patchParam.Parameter[1])
                .Apply();

            // Assert
            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 4" },
                        new Identifier { System = "http://example.org", Value = "value 3" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 1" },
                    }
                }
            ));
        }

        /// <summary>
        // Move with a path that doesn't resolve should throw an exception
        /// </summary>
        [Fact]
        public void MoveInvalidPath()
        {
            // Arrange
            var patchParam = new Parameters().AddMovePatchParameter("Patient.nothing", 0, 2);

            // Act / Assert
            var builder = new FhirPathPatchBuilder(new Patient()).Build(patchParam);
            Assert.Throws<InvalidOperationException>(builder.Apply);
        }

        /// <summary>
        /// Move on a list that hasnt been initialized on the resource
        /// </summary>
        [Fact]
        public void MoveOnListThatDoesntExist()
        {
            // Arrange
            var patchParam = new Parameters().AddMovePatchParameter("Patient.identifier", 0, 0);

            // Act / Assert
            var builder = new FhirPathPatchBuilder(new Patient()).Build(patchParam);
            Assert.Throws<InvalidOperationException>(builder.Apply);
        }

        /// <summary>
        /// Tests move with source index > list length throws exception
        /// </summary>
        [Fact]
        public void InsertInvalidIndex1()
        {
            // Arrange
            var patchParam = new Parameters().AddMovePatchParameter("Patient.identifier", 3, 0);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" }
                    }
            };

            // Act / Assert
            var builder = new FhirPathPatchBuilder(patientResource).Build(patchParam);
            Assert.Throws<InvalidOperationException>(builder.Apply);
        }

        /// <summary>
        /// Tests move with source index < list length throws exception
        /// </summary>
        [Fact]
        public void InsertInvalidIndex2()
        {
            // Arrange
            var patchParam = new Parameters().AddMovePatchParameter("Patient.identifier", -1, 0);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" }
                    }
            };

            // Act / Assert
            var builder = new FhirPathPatchBuilder(patientResource).Build(patchParam);
            Assert.Throws<InvalidOperationException>(builder.Apply);
        }

        /// <summary>
        /// Tests move with destinaion index > list length throws exception
        /// </summary>
        [Fact]
        public void InsertInvalidIndex3()
        {
            // Arrange
            var patchParam = new Parameters().AddMovePatchParameter("Patient.identifier", 0, 3);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" }
                    }
            };

            // Act / Assert
            var builder = new FhirPathPatchBuilder(patientResource).Build(patchParam);
            Assert.Throws<InvalidOperationException>(builder.Apply);
        }

        /// <summary>
        /// Tests move with destination index < list length throws exception
        /// </summary>
        [Fact]
        public void InsertInvalidIndex4()
        {
            // Arrange
            var patchParam = new Parameters().AddMovePatchParameter("Patient.identifier", 0, -1);
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>
                    {
                        new Identifier { System = "http://example.org", Value = "value 1" },
                        new Identifier { System = "http://example.org", Value = "value 2" },
                        new Identifier { System = "http://example.org", Value = "value 3" }
                    }
            };

            // Act / Assert
            var builder = new FhirPathPatchBuilder(patientResource).Build(patchParam);
            Assert.Throws<InvalidOperationException>(builder.Apply);
        }
    }
}
