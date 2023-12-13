// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.CosmosDb.Features.Operations.Export;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Operations;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Queues;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class CosmosDbExportTests : IClassFixture<CosmosDbFhirStorageTestsFixture>
    {
        private readonly CosmosDbFhirStorageTestsFixture _fixture;
        private readonly ITestOutputHelper _testOutputHelper;
        private readonly ISearchService _searchService;
        private readonly CosmosFhirOperationDataStore _operationDataStore;
        private readonly IQueueClient _queueClient;
        private readonly IFhirStorageTestHelper _fhirStorageTestHelper;
        private readonly byte _queueType = (byte)QueueType.Export;

        public CosmosDbExportTests(CosmosDbFhirStorageTestsFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _fixture = fixture;
            _testOutputHelper = testOutputHelper;
            _searchService = _fixture.GetService<ISearchService>();
            _operationDataStore = _fixture.GetService<CosmosFhirOperationDataStore>();
            _queueClient = _fixture.GetService<IQueueClient>();
            _fhirStorageTestHelper = _fixture.GetService<IFhirStorageTestHelper>();
        }

        [Fact]
        public async Task ExportWorkRegistration()
        {
            try
            {
                await PrepareData(3333); // 1111 patients + 1111 observations + 1111 claims.

                var coordJob = new CosmosExportOrchestratorJob(_queueClient, _searchService);

                await RunExportWithCancel("Patient", coordJob, 2, null); // 2=coord+1 resource type

                await RunExport("Patient,Observation", coordJob, 3, null); // 3=coord+2 resource type

                await RunExport(null, coordJob, 4, null); // 3=coord+3 resource type
            }
            finally
            {
                await CleanupTestResourceDocuments();
            }
        }

        private async Task RunExportWithCancel(string resourceType, CosmosExportOrchestratorJob coordJob, int totalJobs, int? totalJobsAfterFailure)
        {
            var coorId = await RunExport(resourceType, coordJob, totalJobs, totalJobsAfterFailure);
            var record = (await _operationDataStore.GetExportJobByIdAsync(coorId, CancellationToken.None)).JobRecord;
            Assert.Equal(OperationStatus.Running, record.Status);
            record.Status = OperationStatus.Canceled;
            var result = await _operationDataStore.UpdateExportJobAsync(record, null, CancellationToken.None);
            Assert.Equal(OperationStatus.Canceled, result.JobRecord.Status);
            result = await _operationDataStore.GetExportJobByIdAsync(coorId, CancellationToken.None);
            Assert.Equal(OperationStatus.Canceled, result.JobRecord.Status);
        }

        private async Task<string> RunExport(string resourceType, CosmosExportOrchestratorJob coordJob, int totalJobs, int? totalJobsAfterFailure)
        {
            var coordRecord = new ExportJobRecord(new Uri("http://localhost/ExportJob"), ExportJobType.All, ExportFormatTags.ResourceName, resourceType, null, Guid.NewGuid().ToString(), 1, maximumNumberOfResourcesPerQuery: 100);
            var result = await _operationDataStore.CreateExportJobAsync(coordRecord, CancellationToken.None);
            Assert.Equal(OperationStatus.Queued, result.JobRecord.Status);
            var coordId = long.Parse(result.JobRecord.Id);
            var groupId = (await _queueClient.GetJobByIdAsync(_queueType, coordId, false, CancellationToken.None)).GroupId;

            await RunCoordinator(coordJob, coordId, groupId, totalJobs, totalJobsAfterFailure);
            return coordId.ToString();
        }

        private async Task RunCoordinator(CosmosExportOrchestratorJob coordJob, long coordId, long groupId, int totalJobs, int? totalJobsAfterFailure)
        {
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(300));

            var jobInfo = await _queueClient.DequeueAsync(_queueType, "Coord", 60, cts.Token, coordId);

            await coordJob.ExecuteAsync(jobInfo, new Progress<string>(), cts.Token);
            await _queueClient.CompleteJobAsync(jobInfo, true, CancellationToken.None);

            var jobs = (await _queueClient.GetJobByGroupIdAsync(_queueType, groupId, false, cts.Token)).ToList();
            Assert.Equal(totalJobs, jobs.Count);
        }

        private async Task PrepareData(int numberOfResourcesToGenerate)
        {
            await CleanupTestResourceDocuments();

            string[] resourceTypes = new string[] { "Patient", "Observation", "Claim" };

            for (int i = 0; i < numberOfResourcesToGenerate; i++)
            {
                string id = Guid.NewGuid().ToString();
                string resourceType = resourceTypes[i % resourceTypes.Length];

                RawResource rawResource = new(
                    $"{{\"resourceType\" = \"{resourceType}\", \"id\"=\"{id}\"}}",
                    FhirResourceFormat.Json,
                    false);

                var resourceWrapper = new FhirCosmosResourceWrapper(
                    id,
                    "1",
                    resourceType,
                    rawResource,
                    default,
                    DateTimeOffset.Now.AddMinutes(-i),
                    false,
                    false,
                    default,
                    default,
                    default,
                    default);

                await _fixture.Container.CreateItemAsync(resourceWrapper);
            }
        }

        private async Task CleanupTestResourceDocuments()
        {
            var partitionKey = JobGroupWrapper.GetJobInfoPartitionKey((byte)QueueType.Export);
            List<QueryDefinition> queries = new()
            {
                new(@"SELECT c.id FROM root c WHERE c.partitionKey = '" + partitionKey + @"'"),
                new(@"SELECT c.id FROM root c WHERE IS_DEFINED(c.resourceTypeName) AND c.isSystem = false"),
            };
            var queryOptions = new QueryRequestOptions { MaxItemCount = -1 }; // Set the max item count to -1 to fetch all items

            foreach (QueryDefinition query in queries)
            {
                using FeedIterator<dynamic> resultSet = _fixture.Container.GetItemQueryIterator<dynamic>(query, requestOptions: queryOptions);

                var batch = _fixture.Container.CreateTransactionalBatch(new PartitionKey(string.Empty));

                while (resultSet.HasMoreResults)
                {
                    foreach (dynamic response in await resultSet.ReadNextAsync())
                    {
                        batch.DeleteItem(response.id.ToString());
                    }
                }
            }
        }
    }
}
