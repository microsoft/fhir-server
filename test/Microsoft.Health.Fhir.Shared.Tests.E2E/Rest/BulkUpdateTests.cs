// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.Extensions;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Tests.E2E.Extensions;
using Microsoft.Health.Test.Utilities;
using Xunit;
using static Hl7.Fhir.Model.Encounter;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.BulkUpdate)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class BulkUpdateTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly HttpIntegrationTestFixture _fixture;
        private readonly HttpClient _httpClient;
        private readonly TestFhirClient _fhirClient;

        public BulkUpdateTests(HttpIntegrationTestFixture fixture)
        {
            _fixture = fixture;
            _httpClient = fixture.HttpClient;
            _fhirClient = fixture.TestFhirClient;
        }

        [SkippableFact]
        public async Task GivenBulkUpdateRequestWithInvalidSearchParameters_WhenRequested_ThenBadRequestIsReturned()
        {
            CheckBulkUpdateEnabled();
            var patchRequest = new Parameters()
                .AddReplacePatchParameter("Patient.gender", new Code("female"));
            var response = await SendBulkUpdateRequest(
                "tag",
                patchRequest,
                queryParams: new Dictionary<string, string>
                {
                    { "invalidParam", "badRequest" },
                });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [SkippableFact]
        public async Task GivenBulkUpdateRequestWithUnsupportedPatchType_WhenRequested_ThenBadRequestIsReturned()
        {
            CheckBulkUpdateEnabled();
            var patchRequest = new Parameters()
                .AddAddPatchParameter("Patient", "gender", new Code("female"))
                .AddInsertPatchParameter("Patient.nothing", new FhirString("test"), 2);
            var response = await SendBulkUpdateRequest(
                "tag",
                patchRequest);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [SkippableTheory]
        [InlineData("SearchParameter")]
        [InlineData("StructureDefinition")]
        public async Task GivenBulkUpdateRequestWithUnsupportedResourceTypes_WhenRequested_ThenBadRequestIsReturned(string resourceType)
        {
            CheckBulkUpdateEnabled();
            var patchRequest = new Parameters();

            var response = await SendBulkUpdateRequest(
                "tag",
                patchRequest,
                $"{resourceType}/$bulk-update",
                null);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [SkippableTheory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenVariousResourcesOfDifferentTypes_WhenBulkUpdatedAtSystemLevel_ThenAllAreUpdated(bool isParallel)
        {
            CheckBulkUpdateEnabled();

            BulkUpdateResult expectedResults = new BulkUpdateResult();
            expectedResults.ResourcesUpdated.Add("Patient", 2);
            expectedResults.ResourcesUpdated.Add("Location", 1);
            expectedResults.ResourcesUpdated.Add("Organization", 1);

            var patchRequest = new Parameters()
                .AddAddPatchParameter("Patient", "gender", new Code("female"))
                .AddAddPatchParameter("Location", "mode", new Code("instance"))
                .AddAddPatchParameter("Organization", "alias", new FhirString("newOrganization"));

            var queryParam = new Dictionary<string, string>
                {
                    { "_isParallel", isParallel.ToString() },
                };

            ChangeTypeToUpsertPatchParameter(patchRequest);
            await RunBulkUpdateRequest(patchRequest, expectedResults, queryParams: queryParam);
        }

        [SkippableTheory]
        [InlineData("Patient", true)]
        [InlineData("Patient", false)]
        [InlineData("Organization", true)]
        [InlineData("Organization", false)]
        public async Task GivenResourcesOfOneType_WhenBulkUpdatedByType_ThenAllOfThatTypeAreUpdated(string resourceType, bool isParallel)
        {
            CheckBulkUpdateEnabled();
            BulkUpdateResult expectedResults = new BulkUpdateResult();
            expectedResults.ResourcesUpdated.Add(resourceType, 4);
            var patchRequest = new Parameters()
                .AddAddPatchParameter("Patient", "gender", new Code("female"))
                .AddAddPatchParameter("Organization", "alias", new FhirString("newOrganization"));

            var queryParam = new Dictionary<string, string>
                {
                    { "_isParallel", isParallel.ToString() },
                };
            ChangeTypeToUpsertPatchParameter(patchRequest);
            await RunBulkUpdateRequest(patchRequest, expectedResults, true, $"{resourceType}/$bulk-update", queryParams: queryParam);
        }

        [SkippableTheory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenBulkUpdateRequestOnSystemLevel_WhenCompleted_ThenExcludedResourcesAreNotUpdatedAndCountedInIgnoredResources(bool isParallel)
        {
            CheckBulkUpdateEnabled();

            BulkUpdateResult expectedResults = new BulkUpdateResult();
            expectedResults.ResourcesUpdated.Add("Patient", 2);
            expectedResults.ResourcesUpdated.Add("Location", 1);
            expectedResults.ResourcesUpdated.Add("Organization", 1);
            expectedResults.ResourcesIgnored.Add("StructureDefinition", 2);
            expectedResults.ResourcesIgnored.Add("SearchParameter", 2);

            var tag = new Coding(string.Empty, Guid.NewGuid().ToString());

            // Create resources of different types with the same tag
            var structureDefinition = Samples.GetJsonSample<StructureDefinition>("StructureDefinition-us-core-birthsex");
            structureDefinition.Meta = new Meta();
            structureDefinition.Meta.Tag.Add(tag);
            await _fhirClient.CreateAsync(structureDefinition);

            structureDefinition = Samples.GetJsonSample<StructureDefinition>("StructureDefinition-us-core-ethnicity");
            structureDefinition.Meta = new Meta();
            structureDefinition.Meta.Tag.Add(tag);
            await _fhirClient.CreateAsync(structureDefinition);

            var randomName = Guid.NewGuid().ToString().ComputeHash()[28..].ToLower();
            var searchParam = Samples.GetJsonSample<SearchParameter>("SearchParameter-Patient-foo");
            searchParam.Meta = new Meta();
            searchParam.Meta.Tag.Add(tag);
            searchParam.Name = randomName;
            searchParam.Url = searchParam.Url.Replace("foo", randomName);
            searchParam.Code = randomName;
            searchParam.Id = randomName;
            await _fhirClient.CreateAsync(searchParam);

            searchParam = Samples.GetJsonSample<SearchParameter>("SearchParameter-SpecimenStatus");
            searchParam.Meta = new Meta();
            searchParam.Meta.Tag.Add(tag);
            searchParam.Name = randomName;
            searchParam.Url = searchParam.Url.Replace("foo", randomName);
            searchParam.Code = randomName;
            searchParam.Id = randomName;
            await _fhirClient.CreateAsync(searchParam);

            // Create resources of different types with the same tag
            await _fhirClient.CreateResourcesAsync<Patient>(2, tag.Code);
            var location = new Location
            {
                Meta = new Meta
                {
                    Tag = new List<Coding>
                    {
                        new Coding("testTag", tag.Code),
                    },
                },
            };
            await _fhirClient.CreateAsync(location);

            var organization = new Organization
            {
                Meta = new Meta
                {
                    Tag = new List<Coding>
                    {
                        new Coding("testTag", tag.Code),
                    },
                },
                Active = true,
            };
            await _fhirClient.CreateAsync(organization);

            // Wait to ensure resources are created before bulk update
            await Task.Delay(2000);

            var patchRequest = new Parameters()
                .AddAddPatchParameter("Resource", "language", new Code("en"));

            ChangeTypeToUpsertPatchParameter(patchRequest);

            // Create the request with Observation and Location as excluded resource types

            var queryParam = new Dictionary<string, string>
                {
                    { "_isParallel", isParallel.ToString() },
                };
            HttpResponseMessage response = await SendBulkUpdateRequest(tag.Code, patchRequest, "$bulk-update", queryParam);

            // Monitor the job until completion
            await MonitorBulkUpdateJob(response.Content.Headers.ContentLocation, expectedResults);
        }

        [SkippableFact]
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task GivenBulkUpdateJobWithIncludeSearchWithIsParallelTrue_WhenCompleted_ThenIncludedResourcesAreUpdated()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
#if Stu3
    Skip.If(true, "Referenced used isn't present in Stu3");
#else
            CheckBulkUpdateEnabled();
            var tag = new Coding(string.Empty, Guid.NewGuid().ToString());

            // Create resources of different types with the same tag
            var patient = Samples.GetJsonSample<Patient>("Patient");
            patient.Meta = new Meta();
            patient.Meta.Tag.Add(tag);
            patient = await _fhirClient.CreateAsync(patient);

            var encounter = Activator.CreateInstance<Encounter>();
            encounter.Meta = new Meta
            {
                Tag = new List<Coding>
                {
                    new Coding("testTag", tag.Code),
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
            observation.Meta = new Meta
            {
                Tag = new List<Coding>
                {
                    new Coding("testTag", tag.Code),
                },
            };
            observation.Subject = new ResourceReference("Patient/" + patient.Id);
            observation.Encounter = new ResourceReference("Encounter/" + encounter.Id);
            observation.Status = ObservationStatus.Final;
            observation.Code = new CodeableConcept("test", "test");

            await _fhirClient.CreateAsync(observation);

            await Task.Delay(5000); // Ensure resources are created

            var patchRequest = new Parameters()
                .AddReplacePatchParameter("Patient.active", new FhirBoolean(true))
                .AddReplacePatchParameter("Observation.status", new Code("amended"))
                .AddReplacePatchParameter("Encounter.status", new Code("planned"));

            var queryParam = new Dictionary<string, string>
                {
                    { "_include", "Observation:*" },
                    { "_isParallel", "true" },
                };

            HttpResponseMessage response = await SendBulkUpdateRequest(tag.Code, patchRequest, "Observation/$bulk-update", queryParam);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            BulkUpdateResult expectedResults = new BulkUpdateResult();
            expectedResults.ResourcesUpdated.Add("Patient", 1);
            expectedResults.ResourcesUpdated.Add("Observation", 1);
            expectedResults.ResourcesUpdated.Add("Encounter", 1);
            await MonitorBulkUpdateJob(response.Content.Headers.ContentLocation, expectedResults);
#endif
        }

        [SkippableFact]
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task GivenBulkUpdateJobWithIncludeSearchWithIsParallelFalse_WhenCompleted_ThenIncludedResourcesAreUpdated()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
#if Stu3
    Skip.If(true, "Referenced used isn't present in Stu3");
#else
            CheckBulkUpdateEnabled();
            var tag = new Coding(string.Empty, Guid.NewGuid().ToString());

            // Create resources of different types with the same tag
            var patient = Samples.GetJsonSample<Patient>("Patient");
            patient.Meta = new Meta();
            patient.Meta.Tag.Add(tag);
            patient = await _fhirClient.CreateAsync(patient);

            var encounter = Activator.CreateInstance<Encounter>();
            encounter.Meta = new Meta
            {
                Tag = new List<Coding>
                {
                    new Coding("testTag", tag.Code),
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
            observation.Meta = new Meta
            {
                Tag = new List<Coding>
                {
                    new Coding("testTag", tag.Code),
                },
            };
            observation.Subject = new ResourceReference("Patient/" + patient.Id);
            observation.Encounter = new ResourceReference("Encounter/" + encounter.Id);
            observation.Status = ObservationStatus.Final;
            observation.Code = new CodeableConcept("test", "test");

            await _fhirClient.CreateAsync(observation);

            await Task.Delay(5000); // Ensure resources are created

            var patchRequest = new Parameters()
                .AddReplacePatchParameter("Patient.active", new FhirBoolean(true))
                .AddReplacePatchParameter("Observation.status", new Code("amended"))
                .AddReplacePatchParameter("Encounter.status", new Code("planned"));

            var queryParam = new Dictionary<string, string>
                {
                    { "_include", "Observation:*" },
                    { "_isParallel", "false" },
                };

            HttpResponseMessage response = await SendBulkUpdateRequest(tag.Code, patchRequest, "Observation/$bulk-update", queryParam);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            BulkUpdateResult expectedResults = new BulkUpdateResult();
            expectedResults.ResourcesUpdated.Add("Patient", 1);
            expectedResults.ResourcesUpdated.Add("Observation", 1);
            expectedResults.ResourcesUpdated.Add("Encounter", 1);
            await MonitorBulkUpdateJob(response.Content.Headers.ContentLocation, expectedResults);
#endif
        }

        [SkippableTheory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenBulkUpdateJobWithRevincludeSearch_WhenCompleted_ThenIncludedResourcesAreUpdated(bool isParallel)
        {
            CheckBulkUpdateEnabled();
            var tag = new Coding(string.Empty, Guid.NewGuid().ToString());
            var patient = Samples.GetJsonSample<Patient>("Patient");
            patient.Meta = new Meta();
            patient.Meta.Tag.Add(tag);
            patient = await _fhirClient.CreateAsync(patient);

            var encounter = Activator.CreateInstance<Encounter>();
            encounter.Meta = new Meta
            {
                Tag = new List<Coding>
                {
                    new Coding("testTag", tag.Code),
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
            observation.Meta = new Meta
            {
                Tag = new List<Coding>
                {
                    new Coding("testTag", tag.Code),
                },
            };
            observation.Subject = new ResourceReference("Patient/" + patient.Id);
            observation.Status = ObservationStatus.Final;
            observation.Code = new CodeableConcept("test", "test");

            await _fhirClient.CreateAsync(observation);

            await Task.Delay(5000); // Ensure resources are created

            var patchRequest = new Parameters()
                .AddReplacePatchParameter("Patient.active", new FhirBoolean(true))
                .AddReplacePatchParameter("Observation.status", new Code("amended"))
                .AddReplacePatchParameter("Encounter.status", new Code("planned"));
            var queryParam = new Dictionary<string, string>
                {
                    { "_revinclude", "*:*" },
                    { "_isParallel", isParallel.ToString() },
                };

            HttpResponseMessage response = await SendBulkUpdateRequest(tag.Code, patchRequest, "Patient/$bulk-update", queryParam);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            BulkUpdateResult expectedResults = new BulkUpdateResult();
            expectedResults.ResourcesUpdated.Add("Patient", 1);
            expectedResults.ResourcesUpdated.Add("Observation", 1);
            expectedResults.ResourcesUpdated.Add("Encounter", 1);
            await MonitorBulkUpdateJob(response.Content.Headers.ContentLocation, expectedResults);
        }

        [SkippableTheory]
        [InlineData(true)]
        [InlineData(false)]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
        public async Task GivenBulkUpdateJobWithMoreThanOnePageOfResultsWithBulkUpdateOnAllResources_WhenCompleted_ThenAllResourcesAreUpdated(bool isParallel)
        {
            CheckBulkUpdateEnabled();
            var tag = Guid.NewGuid().ToString();
            await CreatePatients(tag, 2005);

            await Task.Delay(5000); // Add delay to ensure resources are created before bulk update

            // Create a patch request that updates a field on Patient
            var patchRequest = new Parameters()
                .AddAddPatchParameter("Patient", "active", new FhirBoolean(true));

            ChangeTypeToUpsertPatchParameter(patchRequest);

            using HttpResponseMessage response = await SendBulkUpdateRequest(tag, patchRequest, "Patient/$bulk-update?_maxCount=500", new Dictionary<string, string>() { { "_isParallel", isParallel.ToString() } });
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            BulkUpdateResult expectedResults = new BulkUpdateResult();
            expectedResults.ResourcesUpdated.Add("Patient", 2005);
            await MonitorBulkUpdateJob(response.Content.Headers.ContentLocation, expectedResults);
        }

        [SkippableTheory]
        [InlineData(true)]
        [InlineData(false)]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
        public async Task GivenBulkUpdateJobWithMoreThanOnePageOfResultsWithNoApplicablePatchRequest_WhenCompleted_ThenResourcesAreIgnored(bool isParallel)
        {
            CheckBulkUpdateEnabled();

            var tag = Guid.NewGuid().ToString();
            await CreatePatients(tag, 2005);

            await Task.Delay(5000); // Add delay to ensure resources are created before bulk update

            // For Ignored resources
            var patchRequest = new Parameters()
                .AddAddPatchParameter("Group", "active", new FhirBoolean(true));

            ChangeTypeToUpsertPatchParameter(patchRequest);
            using HttpResponseMessage responseIgnored = await SendBulkUpdateRequest(tag, patchRequest, "Patient/$bulk-update?_maxCount=500", new Dictionary<string, string>() { { "_isParallel", isParallel.ToString() } });
            Assert.Equal(HttpStatusCode.Accepted, responseIgnored.StatusCode);

            BulkUpdateResult expectedResultsForIgnored = new BulkUpdateResult();
            expectedResultsForIgnored.ResourcesIgnored.Add("Patient", 2005);
            await MonitorBulkUpdateJob(responseIgnored.Content.Headers.ContentLocation, expectedResultsForIgnored);
        }

        [SkippableTheory]
        [InlineData(true)]
        [InlineData(false)]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
        public async Task GivenBulkUpdateJobWithMoreThanOnePageOfResultsWhenPatchFails_WhenCompleted_ThenCorrectCountIsReturnedWithPatchFailed(bool isParallel)
        {
            CheckBulkUpdateEnabled();
            var tag = Guid.NewGuid().ToString();
            await CreatePatients(tag, 2005);

            await Task.Delay(5000); // Add delay to ensure resources are created before bulk update

            // For Patch failures
            var patchRequest = new Parameters()
                .AddAddPatchParameter("Patient", "id", new FhirString("newId"))
                .AddAddPatchParameter("Group", "active", new FhirBoolean(true));

            ChangeTypeToUpsertPatchParameter(patchRequest);

            using HttpResponseMessage responsePatchFailed = await SendBulkUpdateRequest(tag, patchRequest, "Patient/$bulk-update?_maxCount=500", new Dictionary<string, string>() { { "_isParallel", isParallel.ToString() } });
            Assert.Equal(HttpStatusCode.Accepted, responsePatchFailed.StatusCode);

            BulkUpdateResult expectedResultsPatchFailed = new BulkUpdateResult();
            expectedResultsPatchFailed.ResourcesPatchFailed.Add("Patient", 2005);
            await MonitorBulkUpdateJob(responsePatchFailed.Content.Headers.ContentLocation, expectedResultsPatchFailed);
        }

        [SkippableTheory]
        [InlineData(true)]
        [InlineData(false)]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
        public async Task GivenBulkUpdateJobWithMoreThanOnePageOfIncludeResultsWithBulkUpdateOnAllResources_WhenCompleted_ThenAllResourcesAreUpdated(bool isParallel)
        {
            CheckBulkUpdateEnabled();
            var tag = Guid.NewGuid().ToString();
            await CreateGroupWithPatients(tag, 2005);

            await Task.Delay(5000); // Add delay to ensure resources are created before bulk update

            // Create a patch request that updates a field on both Patient and Group
            var patchRequest = new Parameters()
                .AddAddPatchParameter("Patient", "active", new FhirBoolean(false))
                .AddAddPatchParameter("Group", "active", new FhirBoolean(false));

            ChangeTypeToUpsertPatchParameter(patchRequest);
            var queryParam = new Dictionary<string, string>
                {
                    { "_include", "Group:member" },
                    { "_isParallel", isParallel.ToString() },
                };

            using HttpResponseMessage response = await SendBulkUpdateRequest(tag, patchRequest, "Group/$bulk-update?_maxCount=500", queryParam);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            BulkUpdateResult expectedResults = new BulkUpdateResult();
            expectedResults.ResourcesUpdated.Add("Patient", 2005);
            expectedResults.ResourcesUpdated.Add("Group", 1);
            await MonitorBulkUpdateJob(response.Content.Headers.ContentLocation, expectedResults);
        }

        [SkippableTheory]
        [InlineData(true)]
        [InlineData(false)]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
        public async Task GivenBulkUpdateJobWithMoreThanOnePageOfIncludeResultsWithNoApplicablePatchRequest_WhenCompleted_ThenResourcesAreIgnored(bool isParallel)
        {
            CheckBulkUpdateEnabled();
            var tag = Guid.NewGuid().ToString();
            await CreateGroupWithPatients(tag, 2005);

            await Task.Delay(5000); // Add delay to ensure resources are created before bulk update

            // For Ignored resources
            var patchRequest = new Parameters()
                .AddAddPatchParameter("Group", "active", new FhirBoolean(true));
            ChangeTypeToUpsertPatchParameter(patchRequest);
            var queryParam = new Dictionary<string, string>
                {
                    { "_include", "Group:member" },
                    { "_isParallel", isParallel.ToString() },
                };
            using HttpResponseMessage responseIgnored = await SendBulkUpdateRequest(tag, patchRequest, "Group/$bulk-update?_maxCount=500", queryParam);
            Assert.Equal(HttpStatusCode.Accepted, responseIgnored.StatusCode);

            BulkUpdateResult expectedResultsForIgnored = new BulkUpdateResult();
            expectedResultsForIgnored.ResourcesIgnored.Add("Patient", 2005);
            expectedResultsForIgnored.ResourcesUpdated.Add("Group", 1);
            await MonitorBulkUpdateJob(responseIgnored.Content.Headers.ContentLocation, expectedResultsForIgnored);
        }

        [SkippableTheory]
        [InlineData(true)]
        [InlineData(false)]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
        public async Task GivenBulkUpdateJobWithMoreThanOnePageOfIncludeResultsWhenPatchFails_WhenCompleted_ThenCorrectCountIsReturnedWithPatchFailed(bool isParallel)
        {
            CheckBulkUpdateEnabled();
            var tag = Guid.NewGuid().ToString();
            await CreateGroupWithPatients(tag, 2005);

            await Task.Delay(5000); // Add delay to ensure resources are created before bulk update

            // For Patch failures
            var patchRequest = new Parameters()
                .AddAddPatchParameter("Patient", "id", new FhirString("newId"))
                .AddAddPatchParameter("Group", "active", new FhirBoolean(true));
            ChangeTypeToUpsertPatchParameter(patchRequest);
            var queryParam = new Dictionary<string, string>
                {
                    { "_include", "Group:member" },
                    { "_isParallel", isParallel.ToString() },
                };
            using HttpResponseMessage responsePatchFailed = await SendBulkUpdateRequest(tag, patchRequest, "Group/$bulk-update?_maxCount=500", queryParam);
            Assert.Equal(HttpStatusCode.Accepted, responsePatchFailed.StatusCode);

            BulkUpdateResult expectedResultsPatchFailed = new BulkUpdateResult();
            expectedResultsPatchFailed.ResourcesPatchFailed.Add("Patient", 2005);
            expectedResultsPatchFailed.ResourcesUpdated.Add("Group", 1);
            await MonitorBulkUpdateJob(responsePatchFailed.Content.Headers.ContentLocation, expectedResultsPatchFailed);
        }

        [SkippableTheory]
        [InlineData(true)]
        [InlineData(false)]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
        public async Task GivenBulkUpdateJobWithMoreThanOnePageOfIncludeResultsOnMultiplePages_WhenCompleted_ThenResultsAreMatched(bool isParallel)
        {
            CheckBulkUpdateEnabled();
            var tag = Guid.NewGuid().ToString();
            await CreateGroupWithPatients(tag, 2005);

            // For included resources present on different pages
            // Create chunk of Group resources with same tag
            await CreateGroups(tag, 1010);

            // Create Group with included Patients
            await CreateGroupWithPatients(tag, 2005);

            await Task.Delay(5000); // Add delay to ensure resources are created before bulk update
            var queryParam = new Dictionary<string, string>
                {
                    { "_include", "Group:member" },
                    { "_isParallel", isParallel.ToString() },
                };
            var patchRequest = new Parameters()
                .AddAddPatchParameter("Patient", "active", new FhirBoolean(true))
                .AddAddPatchParameter("Group", "active", new FhirBoolean(true));
            ChangeTypeToUpsertPatchParameter(patchRequest);
            using HttpResponseMessage responseForIncludedResultsOnDifferentPages = await SendBulkUpdateRequest(tag, patchRequest, "Group/$bulk-update?_maxCount=500", queryParam);
            Assert.Equal(HttpStatusCode.Accepted, responseForIncludedResultsOnDifferentPages.StatusCode);

            BulkUpdateResult expectedResultsForIncludedResultsOnDifferentPages = new BulkUpdateResult();
            expectedResultsForIncludedResultsOnDifferentPages.ResourcesUpdated.Add("Patient", 4010);
            expectedResultsForIncludedResultsOnDifferentPages.ResourcesUpdated.Add("Group", 1012);
            await MonitorBulkUpdateJob(responseForIncludedResultsOnDifferentPages.Content.Headers.ContentLocation, expectedResultsForIncludedResultsOnDifferentPages);
        }

        private async Task RunBulkUpdateRequest(
            Parameters patchRequest,
            BulkUpdateResult expectedResults,
            bool addUnupdatedResource = false,
            string path = "$bulk-update",
            Dictionary<string, string> queryParams = null,
            string tagInput = null)
        {
            if (addUnupdatedResource)
            {
                expectedResults.ResourcesUpdated.Add("Device", 1); // Add one that shouldn't be updated
            }

            string tag = tagInput ?? Guid.NewGuid().ToString();
            foreach (var key in expectedResults.ResourcesUpdated.Keys)
            {
                await _fhirClient.CreateResourcesAsync(ModelInfoProvider.GetTypeForFhirType(key), (int)expectedResults.ResourcesUpdated[key], tag);
            }

            await Task.Delay(2000); // Add delay to ensure resources are created before bulk update

            HttpResponseMessage response = await SendBulkUpdateRequest(tag, patchRequest, path, queryParams);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            if (addUnupdatedResource)
            {
                expectedResults.ResourcesUpdated.Remove("Device");
            }

            await MonitorBulkUpdateJob(response.Content.Headers.ContentLocation, expectedResults);
        }

        private async Task<HttpResponseMessage> SendBulkUpdateRequest(
            string tag,
            Parameters patchRequest,
            string path = "$bulk-update",
            Dictionary<string, string> queryParams = null)
        {
            if (queryParams == null)
            {
                queryParams = new Dictionary<string, string>();
            }

            queryParams.Add("_tag", tag);

            path = QueryHelpers.AddQueryString(path, queryParams);
            var requestUri = new Uri(_httpClient.BaseAddress, path);

            return await _fhirClient.BulkUpdateAsync(requestUri.ToString(), patchRequest, CancellationToken.None);
        }

        private async Task MonitorBulkUpdateJob(Uri location, BulkUpdateResult expectedResults)
        {
            // Implement logic to poll the job status and verify results
            // This is a placeholder; adapt to your actual bulk update job monitoring
            var result = (await _fhirClient.WaitForBulkJobStatus("Bulk update", location)).Resource;

            var resultsChecked = 0;
            foreach (var parameter in result.Parameter)
            {
                if (parameter.Name == "ResourceUpdatedCount")
                {
                    foreach (var part in parameter.Part)
                    {
                        var resourceName = part.Name;
                        var numberUpdated = (long)((Integer64)part.Value).Value;

                        Assert.Equal(expectedResults.ResourcesUpdated[resourceName], numberUpdated);
                        resultsChecked++;
                    }
                }
                else if (parameter.Name == "ResourcePatchFailedCount")
                {
                    foreach (var part in parameter.Part)
                    {
                        var resourceName = part.Name;
                        var numberPatchFailed = (long)((Integer64)part.Value).Value;

                        Assert.Equal(expectedResults.ResourcesPatchFailed[resourceName], numberPatchFailed);
                        resultsChecked++;
                    }
                }
                else if (parameter.Name == "ResourceIgnoredCount")
                {
                    foreach (var part in parameter.Part)
                    {
                        var resourceName = part.Name;
                        var numberIgnored = (long)((Integer64)part.Value).Value;

                        Assert.Equal(expectedResults.ResourcesIgnored[resourceName], numberIgnored);
                        resultsChecked++;
                    }
                }
            }

            int expectedTotal = expectedResults.ResourcesUpdated.Count
                  + expectedResults.ResourcesPatchFailed.Count
                  + expectedResults.ResourcesIgnored.Count;
            Assert.Equal(expectedTotal, resultsChecked);
        }

        private void CheckBulkUpdateEnabled()
        {
            var supported = _fixture.TestFhirServer.Metadata.SupportsOperation("bulk-update");
            Console.WriteLine($"Bulk update operation supported: {supported}");
            Skip.IfNot(supported, "$bulk-update not enabled on this server");
        }

        private async Task CreateGroupWithPatients(string tag, int count)
        {
            Bundle createBundle = new Bundle();
            createBundle.Type = Bundle.BundleType.Batch;
            createBundle.Entry = new List<Bundle.EntryComponent>();

            Group group = new Group();
            group.Member = new List<Group.MemberComponent>();
#if !R5
            group.Actual = true;
#else
            group.Membership = Group.GroupMembershipBasis.Enumerated;
#endif
            group.Type = Group.GroupType.Person;

            group.Meta = new Meta();
            group.Meta.Tag = new List<Coding> { new Coding("http://e2etests", tag) };

            for (int i = 0; i < count; i++)
            {
                var id = Guid.NewGuid();
                var patient = new Patient();
                patient.Meta = new Meta();
                patient.Meta.Tag = new List<Coding> { new Coding("http://e2etests", tag) };
                patient.Id = id.ToString();

                createBundle.Entry.Add(new Bundle.EntryComponent { Resource = patient, Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.PUT, Url = $"Patient/{id}" } });

                group.Member.Add(new Group.MemberComponent { Entity = new ResourceReference($"Patient/{id}") });

                if (i > 0 && i % 490 == 0)
                {
                    // Since bundles can only hold 500 resources Patients need to be made in multiple calls
                    using FhirResponse<Bundle> subResponse = await _fhirClient.PostBundleAsync(createBundle);
                    Assert.Equal(HttpStatusCode.OK, subResponse.StatusCode);

                    createBundle = new Bundle();
                    createBundle.Type = Bundle.BundleType.Batch;
                    createBundle.Entry = new List<Bundle.EntryComponent>();
                }
            }

            createBundle.Entry.Add(new Bundle.EntryComponent { Resource = group, Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.POST, Url = "Group" } });

            using FhirResponse<Bundle> response = await _fhirClient.PostBundleAsync(createBundle);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        private async Task CreateGroups(string tag, int count)
        {
            Bundle createBundle = new Bundle();
            createBundle.Type = Bundle.BundleType.Batch;
            createBundle.Entry = new List<Bundle.EntryComponent>();

            for (int i = 0; i < count; i++)
            {
                var id = Guid.NewGuid();
                Group group = new Group();
#if !R5
                group.Actual = true;
#else
                group.Membership = Group.GroupMembershipBasis.Enumerated;
#endif
                group.Type = Group.GroupType.Person;

                group.Meta = new Meta();
                group.Meta.Tag = new List<Coding> { new Coding("http://e2etests", tag) };
                group.Id = id.ToString();

                createBundle.Entry.Add(new Bundle.EntryComponent { Resource = group, Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.PUT, Url = $"Group/{id}" } });

                if (i > 0 && i % 490 == 0)
                {
                    // Since bundles can only hold 500 resources Patients need to be made in multiple calls
                    using FhirResponse<Bundle> subResponse = await _fhirClient.PostBundleAsync(createBundle);
                    Assert.Equal(HttpStatusCode.OK, subResponse.StatusCode);

                    createBundle = new Bundle();
                    createBundle.Type = Bundle.BundleType.Batch;
                    createBundle.Entry = new List<Bundle.EntryComponent>();
                }
            }

            using FhirResponse<Bundle> response = await _fhirClient.PostBundleAsync(createBundle);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        private async Task CreatePatients(string tag, int count)
        {
            Bundle createBundle = new Bundle();
            createBundle.Type = Bundle.BundleType.Batch;
            createBundle.Entry = new List<Bundle.EntryComponent>();

            for (int i = 0; i < count; i++)
            {
                var id = Guid.NewGuid();
                var patient = new Patient();
                patient.Meta = new Meta();
                patient.Meta.Tag = new List<Coding> { new Coding("http://e2etests", tag) };
                patient.Id = id.ToString();

                createBundle.Entry.Add(new Bundle.EntryComponent { Resource = patient, Request = new Bundle.RequestComponent { Method = Bundle.HTTPVerb.PUT, Url = $"Patient/{id}" } });
                if (i > 0 && i % 490 == 0)
                {
                    // Since bundles can only hold 500 resources Patients need to be made in multiple calls
                    using FhirResponse<Bundle> subResponse = await _fhirClient.PostBundleAsync(createBundle);
                    Assert.Equal(HttpStatusCode.OK, subResponse.StatusCode);

                    createBundle = new Bundle();
                    createBundle.Type = Bundle.BundleType.Batch;
                    createBundle.Entry = new List<Bundle.EntryComponent>();
                }
            }

            using FhirResponse<Bundle> response = await _fhirClient.PostBundleAsync(createBundle);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        private static Parameters ChangeTypeToUpsertPatchParameter(Parameters patchRequest)
        {
            // Find the operation parameter (usually named "operation")
            foreach (var op in patchRequest.Parameter.Where(p => p.Name == "operation"))
            {
                // Find the "type" part and update its value
                var typePart = op.Part.FirstOrDefault(p => p.Name == "type");
                if (typePart != null)
                {
                    typePart.Value = new FhirString("upsert");
                }
                else
                {
                    // If not found, add a new "type" part
                    op.Part.Add(new Parameters.ParameterComponent
                    {
                        Name = "type",
                        Value = new FhirString("upsert"),
                    });
                }
            }

            return patchRequest;
        }
    }
}
