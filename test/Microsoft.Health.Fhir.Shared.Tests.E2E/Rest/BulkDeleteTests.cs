// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Export)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class BulkDeleteTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly HttpIntegrationTestFixture _fixture;
        private readonly HttpClient _httpClient;
        private readonly TestFhirClient _fhirClient;

        public BulkDeleteTests(HttpIntegrationTestFixture fixture)
        {
            _fixture = fixture;
            _httpClient = fixture.HttpClient;
            _fhirClient = fixture.TestFhirClient;
        }

        /*
        [SkippableFact]
        public async System.Threading.Tasks.Task GivenVariousResourcesOfDifferentTypes_WhenBulkDeleted_ThenAllAreDeleted()
        {
            CheckBulkDeleteEnabled();

            var resourceTypes = new Dictionary<string, int>
            {
                { "Patient", 2 },
                { "Location", 1 },
                { "Organization", 1 },
            };

            await RunBulkDeleteRequest(resourceTypes);
        }

        [SkippableTheory]
        [InlineData("Patient")]
        [InlineData("Organization")]
        public async System.Threading.Tasks.Task GivenResourcesOfOneType_WhenBulkDeletedByType_ThenAllOfThatTypeAreDeleted(string resourceType)
        {
            CheckBulkDeleteEnabled();

            var resourceTypes = new Dictionary<string, int>()
            {
                { resourceType, 4 },
            };

            await RunBulkDeleteRequest(resourceTypes, true, $"{resourceType}/$bulk-delete");
        }

        [SkippableFact]
        public async System.Threading.Tasks.Task GivenBulkDeleteRequestWithInvalidSearchParameters_WhenRequested_ThenBadRequestIsReturned()
        {
            CheckBulkDeleteEnabled();

            var request = GenerateBulkDeleteRequest(
                "tag",
                queryParams: new Dictionary<string, string>()
                {
                    { "invalidParam", "badRequest" },
                });

            var response = await _httpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [SkippableFact]
        public async System.Threading.Tasks.Task GivenSoftBulkDeleteRequest_WhenCompleted_ThenHistoricalRecordsExist()
        {
            CheckBulkDeleteEnabled();

            var resourceTypes = new Dictionary<string, int>()
            {
                { "Patient", 1 },
            };

            string tag = Guid.NewGuid().ToString();
            var resource = (await _fhirClient.CreateResourcesAsync<Patient>(1, tag)).FirstOrDefault();

            using HttpRequestMessage request = GenerateBulkDeleteRequest(tag);

            using HttpResponseMessage response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            await MonitorBulkDeleteJob(response.Content.Headers.ContentLocation, resourceTypes);

            var history = await _fhirClient.SearchAsync($"Patient/{resource.Id}/_history");
            Assert.Equal(2, history.Resource.Entry.Count);
        }

        [SkippableFact]
        public async System.Threading.Tasks.Task GivenHardBulkDeleteRequest_WhenCompleted_ThenHistoricalRecordsDontExist()
        {
            CheckBulkDeleteEnabled();

            var resourceTypes = new Dictionary<string, int>()
            {
                { "Patient", 1 },
            };

            string tag = Guid.NewGuid().ToString();
            var resource = (await _fhirClient.CreateResourcesAsync<Patient>(1, tag)).FirstOrDefault();

            using HttpRequestMessage request = GenerateBulkDeleteRequest(
                tag,
                queryParams: new Dictionary<string, string>
                {
                    { "_hardDelete", "true" },
                });

            using HttpResponseMessage response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            await MonitorBulkDeleteJob(response.Content.Headers.ContentLocation, resourceTypes);

            await Assert.ThrowsAsync<FhirClientException>(async () => await _fhirClient.SearchAsync($"Patient/{resource.Id}/_history"));
        }

        [SkippableFact]
        public async System.Threading.Tasks.Task GivenPurgeBulkDeleteRequest_WhenCompleted_ThenHistoricalRecordsDontExistAndCurrentRecordExists()
        {
            CheckBulkDeleteEnabled();

            var resourceTypes = new Dictionary<string, int>()
            {
                { "Patient", 1 },
            };

            string tag = Guid.NewGuid().ToString();
            var resource = (await _fhirClient.CreateResourcesAsync<Patient>(1, tag)).FirstOrDefault();

            // Add a second version
            resource.Active = true;
            resource = await _fhirClient.UpdateAsync(resource);

            using HttpRequestMessage request = GenerateBulkDeleteRequest(
                tag,
                queryParams: new Dictionary<string, string>()
                {
                    { "_purgeHistory", "true" },
                });

            using HttpResponseMessage response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            await MonitorBulkDeleteJob(response.Content.Headers.ContentLocation, resourceTypes);

            var history = await _fhirClient.SearchAsync($"Patient/{resource.Id}/_history");
            Assert.Single(history.Resource.Entry);

            var current = await _fhirClient.ReadAsync<Patient>(ResourceType.Patient, resource.Id);
            Assert.Equal(resource.VersionId, current.Resource.VersionId);
        }

        private async System.Threading.Tasks.Task RunBulkDeleteRequest(
            Dictionary<string, int> expectedResults,
            bool addUndeletedResource = false,
            string path = "$bulk-delete",
            Dictionary<string, string> queryParams = null)
        {
            if (addUndeletedResource)
            {
                expectedResults.Add("Device", 1); // Add one that shouldn't be deleted
            }

            string tag = Guid.NewGuid().ToString();
            foreach (var key in expectedResults.Keys)
            {
                await _fhirClient.CreateResourcesAsync(ModelInfoProvider.GetTypeForFhirType(key), expectedResults[key], tag);
            }

            using HttpRequestMessage request = GenerateBulkDeleteRequest(tag, path, queryParams);

            using HttpResponseMessage response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            if (addUndeletedResource)
            {
                expectedResults.Remove("Device");
            }

            await MonitorBulkDeleteJob(response.Content.Headers.ContentLocation, expectedResults);
        }

        private HttpRequestMessage GenerateBulkDeleteRequest(
            string tag,
            string path = "$bulk-delete",
            Dictionary<string, string> queryParams = null)
        {
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Delete,
            };

            request.Headers.Add(KnownHeaders.Prefer, "respond-async");

            if (queryParams == null)
            {
                queryParams = new Dictionary<string, string>();
            }

            queryParams.Add("_tag", tag);

            path = QueryHelpers.AddQueryString(path, queryParams);
            request.RequestUri = new Uri(_httpClient.BaseAddress, path);

            return request;
        }

        private async System.Threading.Tasks.Task MonitorBulkDeleteJob(Uri location, Dictionary<string, int> expectedResults)
        {
            var result = (await _fhirClient.WaitForBulkDeleteStatus(location)).Resource;

            var resultsChecked = 0;
            var issuesChecked = 0;
            foreach (var parameter in result.Parameter)
            {
                if (parameter.Name == "Issues")
                {
                    issuesChecked++;
                }
                else if (parameter.Name == "ResourceDeletedCount")
                {
                    foreach (var part in parameter.Part)
                    {
                        var resourceName = part.Name;
                        var numberDeleted = (int)((FhirDecimal)part.Value).Value;

                        Assert.Equal(expectedResults[resourceName], numberDeleted);
                        resultsChecked++;
                    }
                }
                else
                {
                    throw new Exception($"Unexpected parameter {parameter.Name}");
                }
            }

            Assert.Equal(expectedResults.Keys.Count, resultsChecked);
        }

        private void CheckBulkDeleteEnabled()
        {
            Skip.IfNot(_fixture.TestFhirServer.Metadata.Predicate("CapabilityStatement.rest.operation.where(name='bulk-delete').exists()"), "$bulk-delete not enabled on this server");
        }
        */
    }
}
