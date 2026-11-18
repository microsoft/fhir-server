// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.GetJobStatus;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.SqlServer.Features.Operations;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.JobManagement;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class SqlJobStatusServiceTests : IClassFixture<SqlServerFhirStorageTestsFixture>, IAsyncLifetime
    {
        private readonly SqlServerFhirStorageTestsFixture _fixture;
        private readonly SqlJobStatusService _service;

        public SqlJobStatusServiceTests(SqlServerFhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
            var resolver = Substitute.For<IUrlResolver>();
            resolver.ResolveOperationResultUrl(Arg.Any<string>(), Arg.Any<string>())
                .Returns(call => new Uri($"https://localhost/_operations/{call.ArgAt<string>(0)}/{call.ArgAt<string>(1)}"));
            _service = new SqlJobStatusService(fixture.SqlConnectionWrapperFactory, resolver, fixture.SchemaInformation);
        }

        public async ValueTask InitializeAsync()
        {
            await ExecuteAsync(@"
                DELETE FROM dbo.JobQueue;
                INSERT INTO dbo.JobQueue (QueueType, GroupId, JobId, Definition, DefinitionHash, Status, CreateDate, EndDate)
                SELECT Q, G, J, 'job-status-test', 0x01, S, C, E
                FROM (VALUES
                    (1,110,110,0,CONVERT(datetime,'20260101'),CONVERT(datetime,NULL)),
                    (1,110,111,2,CONVERT(datetime,'20260102'),CONVERT(datetime,'20260103')),
                    (2,110,110,2,CONVERT(datetime,'20260101'),CONVERT(datetime,'20260102')),
                    (6,109,109,1,CONVERT(datetime,'20260101'),CONVERT(datetime,NULL)),
                    (4,108,108,3,CONVERT(datetime,'20260101'),CONVERT(datetime,'20260102')),
                    (5,107,107,4,CONVERT(datetime,'20260101'),CONVERT(datetime,'20260102')),
                    (1,106,106,5,CONVERT(datetime,'20260101'),CONVERT(datetime,'20260102')),
                    (3,999,999,0,CONVERT(datetime,'20260101'),CONVERT(datetime,NULL))
                ) V(Q,G,J,S,C,E);
                INSERT INTO dbo.JobQueue (QueueType, GroupId, JobId, Definition, DefinitionHash, Status)
                SELECT 1, Id, Id, 'job-status-test', 0x01, 0
                FROM (VALUES (100),(99),(98),(97),(96),(95),(94),(93),(92),(91),(90)) V(Id);");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        [Theory]
        [InlineData(null)]
        [InlineData(122)]
        public async Task GivenAnUnavailableSchema_WhenGettingStatus_ThenTheOperationIsNotImplemented(int? currentVersion)
        {
            // Arrange
            var schema = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max) { Current = currentVersion };
            var service = new SqlJobStatusService(_fixture.SqlConnectionWrapperFactory, Substitute.For<IUrlResolver>(), schema);

            // Act
            var action = () => service.GetAllJobStatusAsync(null, 10, null, null, CancellationToken.None);

            // Assert
            await Assert.ThrowsAsync<OperationNotImplementedException>(action);
        }

        [Fact]
        public async Task GivenMixedJobGroups_WhenGettingStatus_ThenOneGlobalPageAndAggregatedValuesAreReturned()
        {
            // Arrange
            var handler = new GetAllJobStatusHandler(_service, Options.Create(new OperationsConfiguration()));

            // Act
            var first = await handler.HandleAsync(new GetAllJobStatusRequest(), CancellationToken.None);
            var second = await handler.HandleAsync(new GetAllJobStatusRequest(continuationToken: first.ContinuationToken), CancellationToken.None);

            // Assert
            Assert.Equal(new long[] { 110, 110, 109, 108, 107, 100, 99, 98, 97, 96 }, first.Jobs.Select(job => job.GroupId));
            Assert.Equal(new long[] { 95, 94, 93, 92, 91, 90 }, second.Jobs.Select(job => job.GroupId));
            Assert.Null(second.ContinuationToken);
            Assert.Equal(QueueType.Import, first.Jobs[0].QueueType);
            var export = first.Jobs[1];
            Assert.Equal(QueueType.Export, export.QueueType);
            Assert.Equal(JobStatus.Created, export.Status);
            Assert.Equal(new DateTime(2026, 1, 1), export.CreateDate);
            Assert.Equal(new DateTime(2026, 1, 3), export.EndDate);
            Assert.Equal(new Uri("https://localhost/_operations/export/110"), export.ContentLocation);
            Assert.Null(first.Jobs[2].EndDate);
        }

        [Theory]
        [InlineData("export", QueueType.Export, 12)]
        [InlineData("import", QueueType.Import, 1)]
        [InlineData("reindex", QueueType.Reindex, 1)]
        [InlineData("bulk-delete", QueueType.BulkDelete, 1)]
        [InlineData("bulk-update", QueueType.BulkUpdate, 1)]
        public async Task GivenAJobType_WhenPaging_ThenOnlyThatTypeIsReturned(string jobType, QueueType queueType, int expectedCount)
        {
            // Arrange
            var handler = new GetAllJobStatusHandler(_service, Options.Create(new OperationsConfiguration
            {
                JobStatus = new JobStatusConfiguration { PageSize = 2 },
            }));
            var jobs = new List<JobStatusInfo>();
            string token = null;

            // Act
            do
            {
                var response = await handler.HandleAsync(new GetAllJobStatusRequest(jobType, token), CancellationToken.None);
                Assert.InRange(response.Jobs.Count, 1, 2);
                jobs.AddRange(response.Jobs);
                token = response.ContinuationToken;
            }
            while (token != null && jobs.Count <= expectedCount);

            // Assert
            Assert.Null(token);
            Assert.Equal(expectedCount, jobs.Count);
            Assert.All(jobs, job => Assert.Equal(queueType, job.QueueType));
            Assert.Equal(expectedCount, jobs.Select(job => job.GroupId).Distinct().Count());
            Assert.DoesNotContain(jobs, job => job.GroupId == 106);
        }

        [Fact]
        public async Task GivenANewJobBetweenPages_WhenPagingOneAtATime_ThenExistingGroupsAreNotSkippedOrRepeated()
        {
            // Arrange
            var handler = new GetAllJobStatusHandler(_service, Options.Create(new OperationsConfiguration
            {
                JobStatus = new JobStatusConfiguration { PageSize = 1 },
            }));
            var first = await handler.HandleAsync(new GetAllJobStatusRequest(), CancellationToken.None);
            await ExecuteAsync(@"
                INSERT INTO dbo.JobQueue (QueueType, GroupId, JobId, Definition, DefinitionHash)
                VALUES (1, 200, 200, 'new-job', 0x02);");
            var jobs = new List<JobStatusInfo>(first.Jobs);
            var token = first.ContinuationToken;

            // Act
            do
            {
                var response = await handler.HandleAsync(new GetAllJobStatusRequest(continuationToken: token), CancellationToken.None);
                jobs.Add(Assert.Single(response.Jobs));
                token = response.ContinuationToken;
            }
            while (token != null && jobs.Count <= 16);

            // Assert
            Assert.Null(token);
            Assert.Equal(16, jobs.Count);
            Assert.Equal(16, jobs.Select(job => (job.GroupId, job.QueueType)).Distinct().Count());
            Assert.DoesNotContain(jobs, job => job.GroupId == 200 || job.GroupId == 106 || job.GroupId == 999);
            Assert.Equal(QueueType.Import, jobs[0].QueueType);
            Assert.Equal(QueueType.Export, jobs[1].QueueType);
            Assert.Equal(90, jobs.Last().GroupId);
        }

        private async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqlConnection(_fixture.TestConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
