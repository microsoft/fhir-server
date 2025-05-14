// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.Extensions;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using Polly;
using Xunit;
using Xunit.Abstractions;
using static Hl7.Fhir.Model.Encounter;
using Resource = Hl7.Fhir.Model.Resource;
using Task = System.Threading.Tasks.Task;

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
        private readonly ITestOutputHelper _output;

        public BulkDeleteTests(HttpIntegrationTestFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _httpClient = fixture.HttpClient;
            _fhirClient = fixture.TestFhirClient;
            _output = output;
        }

        [SkippableFact]
        public async Task GivenVariousResourcesOfDifferentTypes_WhenBulkDeleted_ThenAllAreDeleted()
        {
            CheckBulkDeleteEnabled();

            var resourceTypes = new Dictionary<string, long>
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
        public async Task GivenResourcesOfOneType_WhenBulkDeletedByType_ThenAllOfThatTypeAreDeleted(string resourceType)
        {
            CheckBulkDeleteEnabled();

            var resourceTypes = new Dictionary<string, long>()
            {
                { resourceType, 4 },
            };

            await RunBulkDeleteRequest(resourceTypes, true, $"{resourceType}/$bulk-delete");
        }

        [SkippableFact]
        public async Task GivenBulkDeleteRequestWithInvalidSearchParameters_WhenRequested_ThenBadRequestIsReturned()
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
        public async Task GivenSoftBulkDeleteRequest_WhenCompleted_ThenHistoricalRecordsExist()
        {
            CheckBulkDeleteEnabled();

            var resourceTypes = new Dictionary<string, long>()
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

        [SkippableTheory]
        [InlineData(KnownQueryParameterNames.BulkHardDelete)]
        [InlineData(KnownQueryParameterNames.HardDelete)]
        public async Task GivenHardBulkDeleteRequest_WhenCompleted_ThenHistoricalRecordsDontExist(string hardDeleteKey)
        {
            CheckBulkDeleteEnabled();

            var resourceTypes = new Dictionary<string, long>()
            {
                { "Patient", 1 },
            };

            string tag = Guid.NewGuid().ToString();
            var resource = (await _fhirClient.CreateResourcesAsync<Patient>(1, tag)).FirstOrDefault();

            using HttpRequestMessage request = GenerateBulkDeleteRequest(
                tag,
                queryParams: new Dictionary<string, string>
                {
                    { hardDeleteKey, "true" },
                });

            using HttpResponseMessage response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            await MonitorBulkDeleteJob(response.Content.Headers.ContentLocation, resourceTypes);

            await Assert.ThrowsAsync<FhirClientException>(async () => await _fhirClient.SearchAsync($"Patient/{resource.Id}/_history"));
        }

        [SkippableFact]
        public async Task GivenPurgeBulkDeleteRequest_WhenCompleted_ThenHistoricalRecordsDontExistAndCurrentRecordExists()
        {
            CheckBulkDeleteEnabled();

            var resourceTypes = new Dictionary<string, long>()
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

        [SkippableFact]
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task GivenBulkDeleteJobWithIncludeSearch_WhenCompleted_ThenIncludedResourcesAreDeleted()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
#if Stu3
            Skip.If(true, "Referenced used isn't present in Stu3");
#else
            CheckBulkDeleteEnabled();

            var resourceTypes = new Dictionary<string, long>()
            {
                { "Patient", 1 },
                { "Observation", 1 },
                { "Encounter", 1 },
            };

            string tag = Guid.NewGuid().ToString();
            var patient = (await _fhirClient.CreateResourcesAsync<Patient>(1, tag)).FirstOrDefault();

            var encounter = Activator.CreateInstance<Encounter>();
            encounter.Meta = new Meta()
            {
                Tag = new List<Coding>
                {
                    new Coding("testTag", tag),
                },
            };
            encounter.Status = EncounterStatus.Planned;
#if !R5
            encounter.Class = new Coding("test", "test");
#else
            encounter.Class = new List<CodeableConcept>();
            encounter.Class.Add(new CodeableConcept("test", "test"));
#endif

            encounter = await _fhirClient.CreateAsync(encounter);

            var observation = Activator.CreateInstance<Observation>();
            observation.Meta = new Meta()
            {
                Tag = new List<Coding>
                {
                    new Coding("testTag", tag),
                },
            };
            observation.Subject = new ResourceReference("Patient/" + patient.Id);
            observation.Encounter = new ResourceReference("Encounter/" + encounter.Id);
            observation.Status = ObservationStatus.Final;
            observation.Code = new CodeableConcept("test", "test");

            await _fhirClient.CreateAsync(observation);

            await Task.Delay(5000); // Add delay to ensure resources are created before bulk delete

            using HttpRequestMessage request = GenerateBulkDeleteRequest(
                tag,
                "Observation/$bulk-delete",
                queryParams: new Dictionary<string, string>
                {
                    { "_include", "Observation:*" },
                });

            using HttpResponseMessage response = await _httpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            await MonitorBulkDeleteJob(response.Content.Headers.ContentLocation, resourceTypes);
#endif
        }

        [SkippableFact]
        public async Task GivenBulkDeleteJobWithRevincludeSearch_WhenCompleted_ThenIncludedResourcesAreDeleted()
        {
            CheckBulkDeleteEnabled();

            var resourceTypes = new Dictionary<string, long>()
            {
                { "Patient", 1 },
                { "Observation", 1 },
                { "Encounter", 1 },
            };

            string tag = Guid.NewGuid().ToString();
            var patient = (await _fhirClient.CreateResourcesAsync<Patient>(1, tag)).FirstOrDefault();

            var encounter = Activator.CreateInstance<Encounter>();
            encounter.Meta = new Meta()
            {
                Tag = new List<Coding>
                {
                    new Coding("testTag", tag),
                },
            };
            encounter.Subject = new ResourceReference("Patient/" + patient.Id);
            encounter.Status = EncounterStatus.Planned;
#if !R5
            encounter.Class = new Coding("test", "test");
#else
            encounter.Class = new List<CodeableConcept>();
            encounter.Class.Add(new CodeableConcept("test", "test"));
#endif

            await _fhirClient.CreateAsync(encounter);

            var observation = Activator.CreateInstance<Observation>();
            observation.Meta = new Meta()
            {
                Tag = new List<Coding>
                {
                    new Coding("testTag", tag),
                },
            };
            observation.Subject = new ResourceReference("Patient/" + patient.Id);
            observation.Status = ObservationStatus.Final;
            observation.Code = new CodeableConcept("test", "test");

            await _fhirClient.CreateAsync(observation);

            await Task.Delay(5000); // Add delay to ensure resources are created before bulk delete

            using HttpRequestMessage request = GenerateBulkDeleteRequest(
                tag,
                "Patient/$bulk-delete",
                queryParams: new Dictionary<string, string>
                {
                    { "_revinclude", "*:*" },
                });

            using HttpResponseMessage response = await _httpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            await MonitorBulkDeleteJob(response.Content.Headers.ContentLocation, resourceTypes);
        }

        [SkippableFact]
        public async Task GivenBulkHardDeleteJobWithIncludeSearch_WhenCompleted_ThenIncludedResourcesAreDeleted()
        {
            CheckBulkDeleteEnabled();

            var resourceTypes = new Dictionary<string, long>()
            {
                { "Patient", 1 },
                { "Observation", 1 },
            };

            string tag = Guid.NewGuid().ToString();
            var patient = (await _fhirClient.CreateResourcesAsync<Patient>(1, tag)).FirstOrDefault();

            var observation = Activator.CreateInstance<Observation>();
            observation.Meta = new Meta()
            {
                Tag = new List<Coding>
                {
                    new Coding("testTag", tag),
                },
            };
            observation.Subject = new ResourceReference("Patient/" + patient.Id);
            observation.Status = ObservationStatus.Final;
            observation.Code = new CodeableConcept("test", "test");

            await _fhirClient.CreateAsync(observation);

            await Task.Delay(5000); // Add delay to ensure resources are created before bulk delete

            using HttpRequestMessage request = GenerateBulkDeleteRequest(
                tag,
                "Observation/$bulk-delete",
                queryParams: new Dictionary<string, string>
                {
                    { "_include", "Observation:subject" },
                    { KnownQueryParameterNames.BulkHardDelete, "true" },
                });

            using HttpResponseMessage response = await _httpClient.SendAsync(request);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            await MonitorBulkDeleteJob(response.Content.Headers.ContentLocation, resourceTypes);
        }

        [SkippableTheory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenBulkDeleteRequest_WhenSearchParametersDeleted_ThenSearchParameterStatusShouldBeUpdated(bool hardDelete)
        {
            CheckBulkDeleteEnabled();

            var resourcesToCreate = new List<Resource>();
            var retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: 12,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(5));
            try
            {
                var tag = Guid.NewGuid().ToString();
                var bundle = (Bundle)TagResources((Bundle)Samples.GetJsonSample("SearchParameter-USCoreIG").ToPoco(), tag);

                // Clean up custom search parameters.
                resourcesToCreate.AddRange(bundle.Entry.Select(x => x.Resource));
                await CleanupAsync(resourcesToCreate);

                // Create search parameter resources.
                var resources = await CreateAsync(resourcesToCreate);

                // Invoke bulk-delete on the search parameters.
                using HttpRequestMessage request = GenerateBulkDeleteRequest(
                    tag,
                    $"{KnownResourceTypes.SearchParameter}/$bulk-delete",
                    queryParams: new Dictionary<string, string>
                    {
                        { KnownQueryParameterNames.BulkHardDelete, hardDelete ? "true" : "false" },
                    });

                using HttpResponseMessage response = await _httpClient.SendAsync(request);
                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

                var resourceTypes = new Dictionary<string, long>()
                {
                    { KnownResourceTypes.SearchParameter, bundle.Entry.Count },
                };

                await CheckBulkDeleteStatusAsync(response.Content.Headers.ContentLocation, resourceTypes);

                // Make sure the search parameters are deleted.
                await EnsureBulkDeleteAsync(resourcesToCreate);

                // Ensure the search parameters were deleted by creating the same search parameters again.
                DebugOutput("Creating search parameters again after bulk-delete...");
                await CreateAsync(resourcesToCreate);
            }
            finally
            {
                await CleanupAsync(resourcesToCreate);
            }

            Task<List<Resource>> CreateAsync(List<Resource> resources)
            {
                return retryPolicy.ExecuteAsync(
                     async () =>
                     {
                         DebugOutput($"Creating {resources.Count} search parameters...");
                         var searchResponse = await _fhirClient.SearchAsync(ResourceType.SearchParameter);
                         Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);

                         var count = searchResponse.Resource?.Entry.Count ?? 0;
                         DebugOutput($"{count} search parameters found in the store.");
                         if (count > 0)
                         {
                             var urls = new StringBuilder();
                             foreach (var entry in searchResponse.Resource.Entry)
                             {
                                 urls.AppendLine(((SearchParameter)entry.Resource).Url);
                             }

                             DebugOutput(urls.ToString());
                         }

                         var resourcesCreated = new List<Resource>();
                         foreach (var resource in resources)
                         {
                             var url = ((SearchParameter)resource).Url;
                             try
                             {
                                 DebugOutput($"Url searching: {url}");
                                 searchResponse = await _fhirClient.SearchAsync(
                                     ResourceType.SearchParameter,
                                     $"url={url}");
                                 Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);

                                 var resourceToCreate = searchResponse.Resource?.Entry?
                                     .Select(x => x.Resource)
                                     .Where(
                                        x =>
                                        {
                                            if ((x.TypeName?.Equals(ResourceType.SearchParameter.ToString(), StringComparison.OrdinalIgnoreCase) ?? false)
                                                && (((SearchParameter)x).Url == url))
                                            {
                                                return true;
                                            }

                                            return false;
                                        })
                                     .FirstOrDefault();
                                 if (resourceToCreate != null)
                                 {
                                     DebugOutput($"Url already exists: {url}");
                                     continue;
                                 }

                                 DebugOutput($"Url creating: {url}");
                                 var response = await _fhirClient.CreateAsync(resource);
                                 Assert.Equal(HttpStatusCode.Created, response.StatusCode);

                                 DebugOutput($"Url created: {url}");
                                 resourcesCreated.Add(resource);
                             }
                             catch (Exception ex)
                             {
                                 DebugOutput($"Url create failed: {url}{Environment.NewLine}{ex}");
                                 await Task.Delay(2000);
                                 throw;
                             }
                         }

                         await Task.Delay(2000);
                         foreach (var resource in resourcesCreated)
                         {
                             var response = await _fhirClient.SearchAsync(
                                 ResourceType.SearchParameter,
                                 $"url={((SearchParameter)resource).Url}");
                             Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                             var resourcesRetrieved = response.Resource?.Entry
                                 .Select(x => x.Resource)
                                 .Where(x => x.TypeName?.Equals(ResourceType.SearchParameter.ToString(), StringComparison.OrdinalIgnoreCase) ?? false)
                                 .ToList();
                             Assert.Single(resourcesRetrieved);
                         }

                         searchResponse = await _fhirClient.SearchAsync(ResourceType.SearchParameter);
                         Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);

                         count = searchResponse.Resource?.Entry.Count ?? 0;
                         DebugOutput($"{count} search parameters found in the store after create.");
                         if (count > 0)
                         {
                             var urls = new StringBuilder();
                             foreach (var entry in searchResponse.Resource.Entry)
                             {
                                 urls.AppendLine(((SearchParameter)entry.Resource).Url);
                             }

                             DebugOutput(urls.ToString());
                         }

                         DebugOutput("Creating search parameters completed.");
                         return resourcesCreated;
                     });
            }

            Task CleanupAsync(List<Resource> resources)
            {
                return retryPolicy.ExecuteAsync(
                    async () =>
                    {
                        DebugOutput($"Cleaning up {resources.Count} search parameters...");
                        var searchResponse = await _fhirClient.SearchAsync(ResourceType.SearchParameter);
                        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);

                        var count = searchResponse.Resource?.Entry.Count ?? 0;
                        DebugOutput($"{count} search parameters found in the store.");
                        if (count > 0)
                        {
                            var urls = new StringBuilder();
                            foreach (var entry in searchResponse.Resource.Entry)
                            {
                                urls.AppendLine(((SearchParameter)entry.Resource).Url);
                            }

                            DebugOutput(urls.ToString());
                        }

                        foreach (var resource in resources)
                        {
                            var url = ((SearchParameter)resource).Url;
                            var response = await _fhirClient.SearchAsync(
                                ResourceType.SearchParameter,
                                $"url={url}");
                            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                            var resourceToDelete = response.Resource?.Entry?
                                .Select(x => x.Resource)
                                .Where(x => x.TypeName?.Equals(ResourceType.SearchParameter.ToString(), StringComparison.OrdinalIgnoreCase) ?? false)
                                .FirstOrDefault();
                            if (resourceToDelete != null)
                            {
                                try
                                {
                                    DebugOutput($"Url deleting: {url}");
                                    await _fhirClient.HardDeleteAsync(resourceToDelete);
                                    DebugOutput($"Url deleted: {url}");
                                }
                                catch (Exception ex)
                                {
                                    DebugOutput($"Url delete failed: {url}{Environment.NewLine}{ex}");
                                    throw;
                                }
                            }
                            else
                            {
                                DebugOutput($"Url not found: {url}");
                            }
                        }

                        await Task.Delay(2000);
                        foreach (var resource in resources)
                        {
                            var response = await _fhirClient.SearchAsync(
                                ResourceType.SearchParameter,
                                $"url={((SearchParameter)resource).Url}");
                            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                            var resourceRetrieved = response.Resource?.Entry
                                .Select(x => x.Resource)
                                .Where(x => x.TypeName?.Equals(ResourceType.SearchParameter.ToString(), StringComparison.OrdinalIgnoreCase) ?? false)
                                .FirstOrDefault();
                            Assert.Null(resourceRetrieved);
                        }

                        searchResponse = await _fhirClient.SearchAsync(ResourceType.SearchParameter);
                        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);

                        count = searchResponse.Resource?.Entry.Count ?? 0;
                        DebugOutput($"{count} search parameters found in the store after cleanup.");
                        if (count > 0)
                        {
                            var urls = new StringBuilder();
                            foreach (var entry in searchResponse.Resource.Entry)
                            {
                                urls.AppendLine(((SearchParameter)entry.Resource).Url);
                            }

                            DebugOutput(urls.ToString());
                        }

                        DebugOutput("Cleaning up search parameters completed.");
                    });
            }

            Task CheckBulkDeleteStatusAsync(Uri location, Dictionary<string, long> expectedResults)
            {
                return retryPolicy.ExecuteAsync(
                     async () =>
                     {
                         DebugOutput($"Checking the bulk-delete status: {location?.AbsolutePath}...");
                         var result = (await _fhirClient.WaitForBulkDeleteStatus(location)).Resource;

                         var actualResults = new Dictionary<string, long>();
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
                                     var numberDeleted = (long)((Integer64)part.Value).Value;
                                     actualResults[resourceName] = numberDeleted;
                                     resultsChecked++;
                                 }
                             }
                             else
                             {
                                 throw new Exception($"Unexpected parameter {parameter.Name}");
                             }
                         }

                         var resultInString = JsonConvert.SerializeObject(result);
                         DebugOutput(resultInString);

                         Assert.Contains(
                             actualResults,
                             x => expectedResults.TryGetValue(x.Key, out var value) && value == x.Value);
                         DebugOutput($"Checking the bulk-delete status completed.");
                     });
            }

            Task EnsureBulkDeleteAsync(List<Resource> resources)
            {
                return retryPolicy.ExecuteAsync(
                    async () =>
                    {
                        DebugOutput($"Checking {resources.Count} search parameters deleted...");
                        var deleted = 0;
                        foreach (var resource in resources)
                        {
                            var url = ((SearchParameter)resource).Url;
                            try
                            {
                                DebugOutput($"Checking url: {url}");
                                await _fhirClient.ReadAsync<SearchParameter>($"{resource.TypeName}/{resource.Id}");
                                DebugOutput($"Url not deleted: {url}");
                            }
                            catch (FhirClientException ex) when (ex.Response?.StatusCode == HttpStatusCode.NotFound || ex.Response?.StatusCode == HttpStatusCode.Gone)
                            {
                                DebugOutput($"Url not deleted: {url}");
                                deleted++;
                            }
                        }

                        Assert.Equal(resources.Count, deleted);
                        DebugOutput($"Checking {resources.Count} search parameters deleted completed.");
                    });
            }
        }

        private async Task RunBulkDeleteRequest(
            Dictionary<string, long> expectedResults,
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
                await _fhirClient.CreateResourcesAsync(ModelInfoProvider.GetTypeForFhirType(key), (int)expectedResults[key], tag);
            }

            await Task.Delay(2000); // Add delay to ensure resources are created before bulk delete

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

        private async Task MonitorBulkDeleteJob(Uri location, Dictionary<string, long> expectedResults, bool failOnIssues = false)
        {
            var result = (await _fhirClient.WaitForBulkDeleteStatus(location)).Resource;

            var actualResults = new Dictionary<string, long>();
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
                        var numberDeleted = (long)((Integer64)part.Value).Value;
                        actualResults[resourceName] = numberDeleted;
                        resultsChecked++;
                    }
                }
                else
                {
                    throw new Exception($"Unexpected parameter {parameter.Name}");
                }
            }

            try
            {
                Assert.Equal(expectedResults.Count, actualResults.Count);
                Assert.Contains(
                    actualResults,
                    x => expectedResults.TryGetValue(x.Key, out var value) && value == x.Value);
            }
            finally
            {
                if (issuesChecked > 0 && failOnIssues)
                {
                    Assert.Fail(JsonConvert.SerializeObject(result));
                }
            }
        }

        private void CheckBulkDeleteEnabled()
        {
            Skip.IfNot(_fixture.TestFhirServer.Metadata.SupportsOperation("bulk-delete"), "$bulk-delete not enabled on this server");
        }

        private Resource TagResources(Bundle bundle, string tag)
        {
            foreach (var entry in bundle.Entry)
            {
                entry.Resource.Meta = new Meta
                {
                    Tag = new List<Coding>
                    {
                        new Coding("testTag", tag),
                    },
                };
            }

            return bundle;
        }

        private void DebugOutput(string message)
        {
#if true
            _output.WriteLine(message);
#endif
        }
    }
}
