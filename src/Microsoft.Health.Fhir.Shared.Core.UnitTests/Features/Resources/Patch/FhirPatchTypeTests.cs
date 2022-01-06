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
    public class FhirPatchTypeTests
    {
        [Fact]
        public void GivenAFhirPatchRequest_WhenUpdatingWithNonMatchingType_ThenInvalidOperationExceptionIsThrown()
        {
            var patchParam = new Parameters().AddAddPatchParameter("Patient", "identifier", new FhirDecimal(-42));

            Assert.Throws<InvalidOperationException>(new FhirPathPatchBuilder(new Patient(), patchParam).Apply);
        }

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

            Patient patchedPatientResource = (Patient)new FhirPathPatchBuilder(patientResource, patchParam).Apply();

            Assert.True(patchedPatientResource.Matches(
                new Patient
                {
                    Name = new List<HumanName>
                    {
                        new HumanName { Use = HumanName.NameUse.Nickname, Text = "Katy Test" },
                        new HumanName { Use = HumanName.NameUse.Official, Text = "Katherine Test" },
                    },
                }));
        }
    }
}
