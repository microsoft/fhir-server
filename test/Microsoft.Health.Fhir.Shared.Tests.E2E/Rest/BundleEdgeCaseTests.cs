﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Bundle)]
    [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.All)]
    public class BundleEdgeCaseTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly TestFhirClient _client;

        public BundleEdgeCaseTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenABundleWithConditionalUpdateByReference_WhenExecutedWithMaximizedConditionalQueryParallelism_RunsTheQueryInParallelOnCosmosDb()
        {
            // #conditionalQueryParallelism

            var bundleOptions = new FhirBundleOptions() { MaximizeConditionalQueryParallelism = true, BundleProcessingLogic = FhirBundleProcessingLogic.Parallel };

            // 1 - Retrieve bundle template from file.
            var bundleWithConditionalReference = Samples.GetJsonSample("Bundle-BatchWithConditionalUpdateByIdentifier");
            var bundle = bundleWithConditionalReference.ToPoco<Bundle>();

            // 2 - Update identifiers with new unique IDs.
            Patient[] patients = new Patient[bundle.Entry.Count];
            for (int i = 0; i < bundle.Entry.Count; i++)
            {
                var patient = bundle.Entry[i].Resource.ToResourceElement().ToPoco<Patient>();
                var patientIdentifier = Guid.NewGuid().ToString();

                patient.Identifier.First().Value = patientIdentifier;

                bundle.Entry[i].Request.Url = $"Patient?identifier=|{patientIdentifier}";

                patients[i] = patient;
            }

            // 3 - Submit bundle to create the first version of all resources.
            FhirResponse<Bundle> bundleResponse1 = await _client.PostBundleAsync(bundle, bundleOptions);

            // 4 - Retrieve the auto-generate ID of all resources.
            var autoGeneratedPatientIds = bundleResponse1.Resource.Entry.Select(x => x.Resource).Select(x => x.Id).ToArray();

            // 5 - Update the resources in the bundle to force the creation of a new version.
            foreach (var patient in patients)
            {
                patient.Text = new Narrative
                {
                    Status = Narrative.NarrativeStatus.Generated,
                    Div = $"<div>Content Updated</div>",
                };
            }

            // 6 - Submit the original bundle once more to force:
            //      * Conditional-queries to scan resources by identifier.
            //      * As this's the original bundle, the resources in the bundle do not have the auto-generate IDs, only the identifiers, which will force the conditional-update based on an identifier.
            FhirResponse<Bundle> bundleResponse2 = await _client.PostBundleAsync(bundle, bundleOptions);

            // 7 - Final asserts:
            //      * Assert if the sequence of patients is the same as in the original bundle.
            //      * Assert if the identifier in the bundle is the same as the identifier returned by the FHIR service.
            //      * Assert if a new resource version was created (it's supposed to be 2 to all resources in the bundle).
            for (int i = 0; i < autoGeneratedPatientIds.Count(); i++)
            {
                string localResourceIdentier = patients[i].Identifier.First().Value;
                string remoteResourceIdentifier = bundleResponse2.Resource.Entry[i].Resource.ToResourceElement().ToPoco<Patient>().Identifier.First().Value;

                Assert.Equal(autoGeneratedPatientIds[i], bundleResponse2.Resource.Entry[i].Resource.Id);
                Assert.Equal(localResourceIdentier, remoteResourceIdentifier);
                Assert.Equal("2", bundleResponse2.Resource.Entry[i].Resource.Meta.VersionId);
            }
        }
    }
}
