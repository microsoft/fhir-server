// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using ResourceType = Hl7.Fhir.Model.ResourceType;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [Trait(Traits.Category, Categories.Search)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public sealed class NotExpressionTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly TestFhirClient _client;

        public NotExpressionTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenANotExpressionPattern_WhenSearched_ThenCompareResponseWithExpectedResults()
        {
            IReadOnlyList<HealthRecordIdentifier> healthRecordIdentifiers = await GetHealthRecordIdentifiersAsync(CancellationToken.None);

            var practitionerRoles = healthRecordIdentifiers.Where(i => i.ResourceType == ResourceType.PractitionerRole.ToString());
            foreach (HealthRecordIdentifier practitioner in practitionerRoles)
            {
                string query1 = $"_id={practitioner.Id}&active:not=false";
                Bundle queryResult1 = await _client.SearchAsync(ResourceType.PractitionerRole, query1);
                Assert.Single(queryResult1.Entry);
                Assert.Equal(practitioner.Id, queryResult1.Entry.Single().Resource.Id);

                string query2 = $"_id={practitioner.Id}&active:not=false&location:missing=false";
                Bundle queryResult2 = await _client.SearchAsync(ResourceType.PractitionerRole, query2);
                Assert.Single(queryResult2.Entry);
                Assert.Equal(practitioner.Id, queryResult2.Entry.Single().Resource.Id);

                string query3 = $"_id={practitioner.Id}&active:not=true";
                Bundle queryResult3 = await _client.SearchAsync(ResourceType.PractitionerRole, query3);
                Assert.Empty(queryResult3.Entry);
            }

            var healthCareServices = healthRecordIdentifiers.Where(i => i.ResourceType == ResourceType.HealthcareService.ToString());
            foreach (HealthRecordIdentifier healthCareService in healthCareServices)
            {
                string query1 = $"location:missing=false&_id={healthCareService.Id}";
                Bundle queryResult1 = await _client.SearchAsync(ResourceType.HealthcareService, query1);
                Assert.Single(queryResult1.Entry);
                Assert.Equal(healthCareService.Id, queryResult1.Entry.Single().Resource.Id);

                string query2 = $"_id={healthCareService.Id}";
                Bundle queryResult2 = await _client.SearchAsync(ResourceType.HealthcareService, query2);
                Assert.Single(queryResult2.Entry);
                Assert.Equal(healthCareService.Id, queryResult2.Entry.Single().Resource.Id);
            }
        }

        private async Task<IReadOnlyList<HealthRecordIdentifier>> GetHealthRecordIdentifiersAsync(CancellationToken cancellationToken)
        {
            string requestBundleAsString = Samples.GetJson("Bundle-ChainingSortAndSearchValidation");
            var parser = new FhirJsonParser();
            var requestBundle = parser.Parse<Bundle>(requestBundleAsString);

            using FhirResponse<Bundle> fhirResponse = await _client.PostBundleAsync(requestBundle, cancellationToken);
            Assert.NotNull(fhirResponse);
            Assert.Equal(HttpStatusCode.OK, fhirResponse.StatusCode);

            var recordIdentifiers = new List<HealthRecordIdentifier>();

            // Ensure all records were ingested.
            Assert.Equal(requestBundle.Entry.Count, fhirResponse.Resource.Entry.Count);
            foreach (Bundle.EntryComponent component in fhirResponse.Resource.Entry)
            {
                Assert.NotNull(component.Response.Status);
                HttpStatusCode httpStatusCode = (HttpStatusCode)Convert.ToInt32(component.Response.Status);
                Assert.True(httpStatusCode == HttpStatusCode.OK || httpStatusCode == HttpStatusCode.Created);

                recordIdentifiers.Add(new HealthRecordIdentifier(component.Resource.TypeName, component.Resource.Id));
            }

            return recordIdentifiers;
        }

        private sealed class HealthRecordIdentifier
        {
            public HealthRecordIdentifier(string resourceType, string id)
            {
                ResourceType = resourceType;
                Id = id;
            }

            public string ResourceType { get; }

            public string Id { get; }
        }
    }
}
