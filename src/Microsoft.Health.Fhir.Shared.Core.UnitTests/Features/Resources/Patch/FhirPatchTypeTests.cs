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

        // Tests using "part" for a predefined type. Part is unnecessary but should still work.
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

        // Not in official test cases but tests choice types.
        [Fact]
        public void GivenAFhirPatchRequest_WhenAddingChoicetype_ThenElementWithPropertypeShouldBePopulated()
        {
            var patchParam1 = new Parameters().AddPatchParameter("add", path: "Patient", name: "deceased", value: new FhirBoolean(true));
            var patchParam2 = new Parameters().AddPatchParameter("add", path: "Patient", name: "deceased", value: new FhirDateTime("2020-12-12"));

            var patchedPatientResource1 = new FhirPathPatchBuilder(new Patient(), patchParam1).Apply() as Patient;
            var expectedPatientResource1 = new Patient
            {
                Deceased = new FhirBoolean(true),
            };

            var patchedPatientResource2 = new FhirPathPatchBuilder(new Patient(), patchParam2).Apply() as Patient;
            var expectedPatientResource2 = new Patient
            {
                Deceased = new FhirDateTime("2020-12-12"),
            };

            Assert.Equal(patchedPatientResource1.ToJson(), expectedPatientResource1.ToJson());
            Assert.Equal(patchedPatientResource2.ToJson(), expectedPatientResource2.ToJson());
        }
    }
}
