// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Web;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    public class ValidateTestFixture : HttpIntegrationTestFixture<Startup>
    {
        public ValidateTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
         : base(dataStore, format, testFhirServerFactory)
        {
        }

        protected override async System.Threading.Tasks.Task OnInitializedAsync()
        {
            // Delete all patients before starting the test.
            await TestFhirClient.DeleteAllResources(ResourceType.ValueSet);
            await TestFhirClient.DeleteAllResources(ResourceType.StructureDefinition);
            await TestFhirClient.DeleteAllResources(ResourceType.CodeSystem);
            await TestFhirClient.DeleteAllResources(ResourceType.ConceptMap);
            var sd = new List<string>() { "StructureDefinition-us-core-birthsex", "StructureDefinition-us-core-ethnicity", "StructureDefinition-us-core-patient", "StructureDefinition-us-core-race", "StructureDefinition-us-core-organization" };
            foreach (var name in sd)
            {
                await TestFhirClient.CreateAsync<StructureDefinition>(Samples.GetJsonSample<StructureDefinition>(name));
            }

            var valueSets = new List<string>() { "ValueSet-detailed-ethnicity", "ValueSet-detailed-race", "ValueSet-detailed-race", "ValueSet-omb-ethnicity-category", "ValueSet-omb-race-category", "ValueSet-us-core-birthsex" };
            foreach (var name in valueSets)
            {
                await TestFhirClient.CreateAsync<ValueSet>(Samples.GetJsonSample<ValueSet>(name));
            }
        }
    }
}
