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
using Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch.Helpers;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Resources.Patch
{
    public class FhirPatchTypeTests
    {
        // Tests wrong input type returns meaningful error.
        [Fact]
        public void GivenAFhirPatchRequest_WhenUpdatingWithNonMatchingType_ThenInvalidOperationExceptionIsThrown()
        {
            var patchParam = new Parameters().AddAddPatchParameter("Patient", "identifier", new FhirDecimal(-42));

            var exception = Assert.Throws<InvalidOperationException>(new FhirPathPatchBuilder(new Patient(), patchParam).Apply);
            Assert.Equal("Invalid input for identifier when processing patch add operation.", exception.Message);
        }

        // Tests insert of datatype to list.
        [Fact]
        public void GivenAFhirPatchRequest_WhenInsertingDataTypeToList_ThenTheDataTypeShouldResolveAndSuccess()
        {
            var patchParam = new Parameters().AddInsertPatchParameter("Patient.name", new HumanName { Use = HumanName.NameUse.Nickname, Text = "Katy Test" }, 0);
            var patientResource = new Patient
            {
                Name = new List<HumanName>
                {
                    new HumanName { Use = HumanName.NameUse.Official, Text = "Katherine Test" },
                },
            };

            var patchedPatientResource = new FhirPathPatchBuilder(patientResource, patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
            {
                Name = new List<HumanName>
                    {
                        new HumanName { Use = HumanName.NameUse.Nickname, Text = "Katy Test" },
                        new HumanName { Use = HumanName.NameUse.Official, Text = "Katherine Test" },
                    },
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        [Fact]
        public void GivenAFhirPatchRequest_WhenAddingObjectTypeAsNestedParts_ThenListShouldBeCreatedWithObject()
        {
            var patchParam = new Parameters().AddPatchParameter("add", path: "Patient", name: "contact", value: new Parameters.ParameterComponent
            {
                Name = "name",
                Part = new List<Parameters.ParameterComponent>()
                    {
                        new Parameters.ParameterComponent()
                        {
                            Name = "family",
                            Value = new FhirString("name"),
                        },
                        new Parameters.ParameterComponent()
                        {
                            Name = "text",
                            Value = new FhirString("a name"),
                        },
                        new Parameters.ParameterComponent()
                        {
                            Name = "period",
                            Value = new Period { End = "2020-01-01" },
                        },
                    },
            });

            var patchedPatientResource = new FhirPathPatchBuilder(new Patient(), patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
            {
                Contact = new List<Patient.ContactComponent>
                    {
                        new Patient.ContactComponent
                        {
                            Name = new HumanName
                            {
                                Family = "name",
                                Text = "a name",
                                Period = new Period { End = "2020-01-01" },
                            },
                        },
                    },
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        // Not in official test cases but tied to special case. The type of the code inside the anonymous object causes weird type inference behavior.
        [Fact]
        public void GivenAFhirPatchRequest_WhenAddingAnonymousTypeToList_ThenTypeWithElementsShouldBePopulated()
        {
            var patchParam = new Parameters().AddPatchParameter("add", path: "Patient", name: "link", value: new List<Parameters.ParameterComponent>()
            {
                new Parameters.ParameterComponent()
                {
                    Name = "type",
                    Value = new Code("replaced-by"),
                },
                new Parameters.ParameterComponent()
                {
                    Name = "other",
                    Value = new ResourceReference("Patient/123"),
                },
            });

            var patchedPatientResource = new FhirPathPatchBuilder(new Patient(), patchParam).Apply() as Patient;
            var expectedPatientResource = new Patient
            {
                Link = new List<Patient.LinkComponent>
                {
                    new Patient.LinkComponent
                    {
                        Type = Patient.LinkType.ReplacedBy,
                        Other = new ResourceReference("Patient/123"),
                    },
                },
            };

            Assert.Equal(patchedPatientResource.ToJson(), expectedPatientResource.ToJson());
        }

        // Implements test case at:
        // https://github.com/FHIR/fhir-test-cases/blob/master/r4/patch/fhir-path-tests.xml#L1381
        // More context:
        // https://chat.fhir.org/#narrow/stream/179166-implementers/topic/FHIRPath.20Patch.20on.20uninitialized.20object.2Flist
        [Fact]
        public void GivenAFhirPatchRequest_WhenAddingToUninitializedObject_ThenInvalidOperationExceptionIsThrown()
        {
            var patchParam = new Parameters().AddAddPatchParameter("Patient.identifier.where(use = 'official').period", "end", new FhirDateTime("2021-07-05"));
            var patientResource = new Patient
            {
                Identifier = new List<Identifier>()
                    {
                        new Identifier() { Use = Identifier.IdentifierUse.Official, Value = "value 3" },
                    },
            };

            var exception = Assert.Throws<InvalidOperationException>(new FhirPathPatchBuilder(patientResource, patchParam).Apply);
            Assert.Equal("No content found at Patient.identifier.where(use = 'official').period when processing patch add operation.", exception.Message);
        }

        // Add operations are special in the use of "name". Testing this with an invalid target.
        [Fact]
        public void GivenAFhirPatchRequest_WhenAddingInvalidName_ThenInvalidOperationExceptionIsThrown()
        {
            var patchParam = new Parameters().AddAddPatchParameter("Patient", "invalid", new FhirDateTime("2021-07-05"));

            var exception = Assert.Throws<InvalidOperationException>(new FhirPathPatchBuilder(new Patient(), patchParam).Apply);
            Assert.Equal("Element invalid not found when processing patch add operation.", exception.Message);
        }
    }
}
