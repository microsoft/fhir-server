// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using static Hl7.Fhir.Model.Bundle;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Bundle)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public class BundleAdvancedTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly TestFhirClient _client;

        public BundleAdvancedTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        [Trait(Traits.Priority, Priority.One)]
        public async Task WhenProcessingMultipleBundlesWithTheSameResource_ThenCreateTheResourcesAsExpected()
        {
            const int numberOfParallelBundles = 4;
            const int numberOfPatientsPerBundle = 4;

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = tokenSource.Token;

            // 1 - Post first patient who is created as the base resource to handle all following parallel operations.
            Patient patient = new Patient()
            {
                Identifier = new List<Identifier> { new Identifier("http://example.org/patient-ids", "12345") },
                Name = new List<HumanName> { new HumanName() { Family = "Doe", Given = new List<string> { "John" } } },
                Gender = AdministrativeGender.Male,
                BirthDate = "1990-01-01",
                Text = new Narrative($"<div>{DateTime.UtcNow.ToString("o")}</div>"),
            };
            var firstPatientResponse = await _client.PostAsync("Patient", patient.ToJson(), cancellationToken);
            Assert.True(firstPatientResponse.Response.IsSuccessStatusCode, "First patient ingestion did not complete as expected.");

            Patient patientCreated = firstPatientResponse.Resource.ToResourceElement().ToPoco<Patient>();

            // 2 - Compose multiple bundles that will run in parallel.
            List<Bundle> bundles = new List<Bundle>();
            for (int i = 0; i < numberOfParallelBundles; i++)
            {
                Bundle bundle = new Bundle() { Type = BundleType.Batch };
                for (int j = 0; j < numberOfPatientsPerBundle; j++)
                {
                    // Create Patient clones in memory.
                    Patient tempPatient = Clone(patientCreated);

                    EntryComponent entryComponent = CreateEntryComponent(tempPatient);

                    bundle.Entry.Add(entryComponent);
                }

                bundles.Add(bundle);
            }

            // 3 - Post bundles in parallel.
            List<Task<Client.FhirResponse<Bundle>>> bundleTasks = new List<Task<Client.FhirResponse<Bundle>>>();
            foreach (var bundle in bundles)
            {
                bundleTasks.Add(_client.PostBundleAsync(bundle, new Client.FhirBundleOptions(), cancellationToken));
            }

            Task.WaitAll(bundleTasks.ToArray());

            // 4 - Validate the response of every bundle.
            foreach (Task<Client.FhirResponse<Bundle>> task in bundleTasks)
            {
                Client.FhirResponse<Bundle> fhirResponse = task.Result;
            }
        }

        private static Patient Clone(Patient patient)
        {
            // Patient does not have a native Clone method.

            Patient clone = new Patient();

            clone.Id = patient.Id;
            clone.Identifier = patient.Identifier;
            clone.Name = patient.Name;
            clone.Gender = patient.Gender;
            clone.BirthDate = patient.BirthDate;
            clone.Text = new Narrative($"<div>Cloned at {DateTime.UtcNow.ToString("o")}.</div>");

            return clone;
        }

        private static EntryComponent CreateEntryComponent(Patient patient)
        {
            EntryComponent entryComponent = new EntryComponent()
            {
                Resource = patient,
                Request = new RequestComponent()
                {
                    Method = HTTPVerb.PUT,
                    Url = $"Patient/{patient.Id}",
                },
            };

            return entryComponent;
        }
    }
}
