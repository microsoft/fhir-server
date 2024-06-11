// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.CosmosDb.Features.Operations.Export;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Operations;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Operations.Export
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class CosmosDbExportTests : IClassFixture<CosmosDbFhirExportTestFixture>
    {
        private readonly CosmosDbFhirStorageTestsFixture _fixture;
        private readonly Func<IScoped<ISearchService>> _searchServiceScopeFactory;
        private readonly CosmosFhirOperationDataStore _operationDataStore;
        private readonly IQueueClient _queueClient;
        private readonly byte _queueType = (byte)QueueType.Export;
        private readonly CosmosExportOrchestratorJob _coordJob;
        private readonly ILogger<CosmosExportOrchestratorJob> _logger;

        public CosmosDbExportTests(CosmosDbFhirExportTestFixture fixture)
        {
            _fixture = fixture;
            _searchServiceScopeFactory = _fixture.GetService<Func<IScoped<ISearchService>>>();
            _operationDataStore = _fixture.GetService<CosmosFhirOperationDataStore>();
            _queueClient = _fixture.GetService<IQueueClient>();
            _logger = Substitute.For<ILogger<CosmosExportOrchestratorJob>>();

            _coordJob = new(_queueClient, _searchServiceScopeFactory, _logger);
        }

        [Fact]
        public async Task GivenAnExportOfPatientResources_WhenQueuedAndCancelled_JobStatusAndCountIsCorrectDuringJob()
        {
            string resourceType = "Patient";
            int totalJobs = 2; // 2=coord+1 resource type
            var coorId = await RunExport(resourceType, _coordJob, totalJobs);
            var record = (await _operationDataStore.GetExportJobByIdAsync(coorId, CancellationToken.None)).JobRecord;

            Assert.Equal(OperationStatus.Running, record.Status);

            record.Status = OperationStatus.Canceled;
            var result = await _operationDataStore.UpdateExportJobAsync(record, null, CancellationToken.None);

            Assert.Equal(OperationStatus.Canceled, result.JobRecord.Status);

            result = await _operationDataStore.GetExportJobByIdAsync(coorId, CancellationToken.None);
            Assert.Equal(OperationStatus.Canceled, result.JobRecord.Status);
        }

        [Fact]
        public async Task GivenAnExportOfPatientObservation_WhenQueued_JobStatusAndCountIsCorrectDuringJob()
        {
            await RunExport("Patient,Observation", _coordJob, 3); // 3=coord+2 resource type
        }

        [Fact]
        public async Task GivenAnExportOfAllTypes_WhenQueued_JobStatusAndCountIsCorrectDuringJob()
        {
            await RunExport(null, _coordJob, 4); // 4=coord+3 resource type
        }

        private async Task<string> RunExport(string resourceType, CosmosExportOrchestratorJob coordJob, int totalJobs)
        {
            var coordRecord = new ExportJobRecord(new Uri("http://localhost/ExportJob"), ExportJobType.All, ExportFormatTags.ResourceName, resourceType, null, Guid.NewGuid().ToString(), 1, maximumNumberOfResourcesPerQuery: 100);
            var result = await _operationDataStore.CreateExportJobAsync(coordRecord, CancellationToken.None);
            Assert.Equal(OperationStatus.Queued, result.JobRecord.Status);
            var coordId = long.Parse(result.JobRecord.Id);
            var groupId = (await _queueClient.GetJobByIdAsync(_queueType, coordId, false, CancellationToken.None)).GroupId;

            await RunCoordinator(coordJob, coordId, groupId, totalJobs);
            return coordId.ToString();
        }

        private async Task RunCoordinator(CosmosExportOrchestratorJob coordJob, long coordId, long groupId, int totalJobs)
        {
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(300));

            var jobInfo = await _queueClient.DequeueAsync(_queueType, "Coord", 60, cts.Token, coordId);

            await coordJob.ExecuteAsync(jobInfo, cts.Token);
            await _queueClient.CompleteJobAsync(jobInfo, true, CancellationToken.None);

            var jobs = (await _queueClient.GetJobByGroupIdAsync(_queueType, groupId, false, cts.Token)).ToList();
            Assert.Equal(totalJobs, jobs.Count);
        }
    }
}
