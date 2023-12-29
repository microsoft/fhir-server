// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using static Hl7.Fhir.Model.Bundle;
using static Hl7.Fhir.Model.OperationOutcome;
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

            var bundleWithConditionalReference = Samples.GetJsonSample("Bundle-BatchWithConditionalUpdateByIdentifier");

            var bundle = bundleWithConditionalReference.ToPoco<Bundle>();
            var patient = bundle.Entry.First().Resource.ToResourceElement().ToPoco<Patient>();
            var patientIdentifier = Guid.NewGuid().ToString();

            patient.Identifier.First().Value = patientIdentifier;
            bundle.Entry.First().Request.Url = $"Patient?identifier=|{patientIdentifier}";

            FhirResponse<Bundle> bundleResponse1 = await _client.PostBundleAsync(bundle, new FhirBundleOptions() { MaximizeConditionalQueryParallelism = true });

            var patientId = bundleResponse1.Resource.Entry.First().Resource.Id;

            patient.Text = new Narrative
            {
                Status = Narrative.NarrativeStatus.Generated,
                Div = $"<div>Content Updated</div>",
            };

            FhirResponse<Bundle> bundleResponse2 = await _client.PostBundleAsync(bundle, new FhirBundleOptions() { MaximizeConditionalQueryParallelism = true });

            Assert.Equal(patientId, bundleResponse2.Resource.Entry[0].Resource.Id);
            Assert.Equal("2", bundleResponse2.Resource.Entry[0].Resource.Meta.VersionId);
        }
    }
}
