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
        private readonly HttpClient _httpClient;
        private readonly TestFhirClient _fhirClient;

        public BulkDeleteTests(HttpIntegrationTestFixture fixture)
        {
            _httpClient = fixture.HttpClient;
            _fhirClient = fixture.TestFhirClient;
        }

        [Fact]
        public async System.Threading.Tasks.Task GivenVariousResourcesOfDifferentTypes_WhenBulkDeleted_ThenAllAreDeleted()
        {
            var resourceTypes = new Dictionary<string, int>()
            {
                { "Patient", 2 },
                { "Location", 1 },
                { "Organization", 1 },
            };

            await RunBulkDeleteRequest(resourceTypes);
        }

        [Theory]
        [InlineData("Patient")]
        [InlineData("Organization")]
        public async System.Threading.Tasks.Task GivenResourcesOfOneType_WhenBulkDeletedByType_ThenAllOfThatTypeAreDeleted(string resourceType)
        {
            var resourceTypes = new Dictionary<string, int>()
            {
                { resourceType, 4 },
            };

            await RunBulkDeleteRequest(resourceTypes, true, $"{resourceType}/$bulk-delete");
        }

        [Fact]
        public async System.Threading.Tasks.Task GivenBulkDeleteRequestWithInvalidSearchParameters_WhenRequested_ThenBadRequestIsReturned()
        {
            var request = GenerateBulkDeleteRequest(
                "tag",
                queryParams: new Dictionary<string, string>()
                {
                    { "invalidParam", "badRequest" },
                });

            var response = await _httpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async System.Threading.Tasks.Task GivenSoftBulkDeleteRequest_WhenCompleted_ThenHistoricalRecordsExist()
        {
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

            await MonitorBulkDeleteJob(tag, response.Content.Headers.ContentLocation, resourceTypes);

            var history = await _fhirClient.SearchAsync($"Patient/{resource.Id}/_history");
            Assert.Equal(2, history.Resource.Entry.Count);
        }

        [Fact]
        public async System.Threading.Tasks.Task GivenHardBulkDeleteRequest_WhenCompleted_ThenHistoricalRecordsDontExist()
        {
            var resourceTypes = new Dictionary<string, int>()
            {
                { "Patient", 1 },
            };

            string tag = Guid.NewGuid().ToString();
            var resource = (await _fhirClient.CreateResourcesAsync<Patient>(1, tag)).FirstOrDefault();

            using HttpRequestMessage request = GenerateBulkDeleteRequest(
                tag,
                queryParams: new Dictionary<string, string>()
                {
                    { "hardDelete", "true" },
                });

            using HttpResponseMessage response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            await MonitorBulkDeleteJob(tag, response.Content.Headers.ContentLocation, resourceTypes);

            await Assert.ThrowsAsync<FhirClientException>(async () => await _fhirClient.SearchAsync($"Patient/{resource.Id}/_history"));
        }

        [Fact]
        public async System.Threading.Tasks.Task GivenPurgeBulkDeleteRequest_WhenCompleted_ThenHistoricalRecordsDontExistAndCurrentRecordExists()
        {
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
                    { "hardDelete", "true" },
                    { "purgeHistory", "true" },
                });

            using HttpResponseMessage response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            await MonitorBulkDeleteJob(tag, response.Content.Headers.ContentLocation, resourceTypes);

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

            Type t = typeof(Device);
            string assemblyQualifiedName = t.AssemblyQualifiedName;

            string tag = Guid.NewGuid().ToString();
            foreach (var key in expectedResults.Keys)
            {
                var type = Type.GetType(assemblyQualifiedName.Replace("Device", key), true);
                var method = typeof(FhirClientExtensions).GetMethods().Where(x => x.Name == "CreateResourcesAsync" && x.GetParameters().Length == 3).FirstOrDefault();
                var reference = method.MakeGenericMethod(type);
                object[] parameters = new object[] { _fhirClient, expectedResults[key], tag };
                await (System.Threading.Tasks.Task)reference.Invoke(null, parameters);
            }

            using HttpRequestMessage request = GenerateBulkDeleteRequest(tag, path, queryParams);

            using HttpResponseMessage response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            if (addUndeletedResource)
            {
                expectedResults.Remove("Device");
            }

            await MonitorBulkDeleteJob(tag, response.Content.Headers.ContentLocation, expectedResults);
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

            if (queryParams == null)
            {
                queryParams = new Dictionary<string, string>();
            }

            queryParams.Add("_tag", tag);

            path = QueryHelpers.AddQueryString(path, queryParams);
            request.RequestUri = new Uri(_httpClient.BaseAddress, path);

            return request;
        }

        private async System.Threading.Tasks.Task MonitorBulkDeleteJob(string tag, Uri location, Dictionary<string, int> expectedResults)
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
                else if (parameter.Name == "Resources Deleted")
                {
                    foreach (var part in parameter.Part)
                    {
                        var resourceName = part.Name;
                        var numberDeleted = (int)((FhirDecimal)part.Value).Value;

                        Assert.Equal(expectedResults[resourceName.ToString()], numberDeleted);
                        resultsChecked++;
                    }
                }
                else
                {
                    throw new Exception($"Unexpected parameter {parameter}");
                }
            }

            Assert.Equal(expectedResults.Keys.Count, resultsChecked);
        }
    }
}
