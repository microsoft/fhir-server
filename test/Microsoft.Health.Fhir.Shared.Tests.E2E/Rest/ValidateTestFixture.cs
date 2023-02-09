// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Web;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    public sealed class ValidateTestFixture : HttpIntegrationTestFixture<Startup>
    {
        public ValidateTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
         : base(dataStore, format, testFhirServerFactory)
        {
        }

        protected override async System.Threading.Tasks.Task OnInitializedAsync()
        {
            // Delete all profile related resources before starting the test suite.
            var sd = new List<string>()
            {
                "StructureDefinition-us-core-birthsex", "StructureDefinition-us-core-ethnicity", "StructureDefinition-us-core-patient",
                "StructureDefinition-us-core-race", "StructureDefinition-us-core-organization", "StructureDefinition-us-core-careplan",
                "StructureDefinition-us-core-genderIdentity",
            };
            foreach (var name in sd)
            {
                await TestFhirClient.UpdateAsync(Samples.GetJsonSample<StructureDefinition>(name));
            }

            var valueSets = new List<string>()
            {
                "ValueSet-detailed-ethnicity", "ValueSet-detailed-race", "ValueSet-omb-ethnicity-category",
                "ValueSet-omb-race-category", "ValueSet-birthsex", "ValueSet-us-core-narrative-status",
            };
            foreach (var name in valueSets)
            {
                await TestFhirClient.UpdateAsync(Samples.GetJsonSample<ValueSet>(name));
            }

            var codeSystem = new List<string>() { "CodeSystem-careplan-category", "CodeSystem-Example", "CodeSystem-cdcrec" };
            foreach (var name in codeSystem)
            {
                await TestFhirClient.UpdateAsync(Samples.GetJsonSample<CodeSystem>(name));
            }
        }
    }
}
