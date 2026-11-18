// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class JobStatusTests : IClassFixture<HttpIntegrationTestFixture<JobStatusTests.StartupForJobStatus>>
    {
        private readonly HttpIntegrationTestFixture<StartupForJobStatus> _fixture;
        private readonly TestFhirClient _client;

        public JobStatusTests(HttpIntegrationTestFixture<StartupForJobStatus> fixture)
        {
            _fixture = fixture;
            _client = fixture.TestFhirClient;
        }

        [Fact]
        public async Task GivenMoreJobsThanOnePage_WhenFollowingTheToken_ThenTheFilteredRemainingJobsAreReturned()
        {
            // Arrange
            Assert.SkipUnless(_fixture.IsUsingInProcTestServer, "Requires an isolated in-proc database for seeding job groups.");
            var server = Assert.IsType<InProcTestFhirServer>(_fixture.TestFhirServer);
            await using (var connection = new SqlConnection(server.ConnectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO dbo.JobQueue (QueueType, GroupId, JobId, Definition, DefinitionHash, Status)
                    SELECT 1, Id, Id, 'job-status-e2e', 0x01, 2
                    FROM (VALUES (101),(102),(103),(104)) V(Id);
                    INSERT INTO dbo.JobQueue (QueueType, GroupId, JobId, Definition, DefinitionHash, Status)
                    VALUES (2,200,200,'job-status-e2e',0x02,2);";
                await command.ExecuteNonQueryAsync();
            }

            // Act
            using var first = await _client.ReadAsync<Parameters>("_operations/jobs?jobType=export");
            var tokenParameter = Assert.Single(first.Resource.Parameter, parameter => parameter.Name == "continuationToken");
            var token = Assert.IsType<FhirString>(tokenParameter.Value).Value;
            using var second = await _client.ReadAsync<Parameters>($"_operations/jobs?jobType=export&continuationToken={Uri.EscapeDataString(token)}");

            // Assert
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var firstJobs = first.Resource.Parameter.Where(parameter => parameter.Part.Count > 0).ToArray();
            Assert.Equal(3, firstJobs.Length);
            Assert.Single(second.Resource.Parameter);
            Assert.DoesNotContain(second.Resource.Parameter, parameter => parameter.Name == "continuationToken");
            var jobs = firstJobs.Concat(second.Resource.Parameter).ToArray();
            Assert.Equal(4, jobs.Select(job => job.Name).Distinct().Count());
            Assert.All(jobs, job => Assert.Equal("export", job.Part.Single(part => part.Name == "type").Value.ToString()));

            using var mismatch = await Assert.ThrowsAsync<FhirClientException>(
                () => _client.ReadAsync<Parameters>($"_operations/jobs?jobType=import&continuationToken={Uri.EscapeDataString(token)}"));
            Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        }

        [Theory]
        [InlineData("jobType=defrag")]
        [InlineData("jobType=1")]
        [InlineData("continuationToken=invalid")]
        public async Task GivenInvalidJobStatusParameters_WhenRequested_ThenBadRequestIsReturned(string query)
        {
            // Arrange
            var url = $"_operations/jobs?{query}";

            // Act
            using var exception = await Assert.ThrowsAsync<FhirClientException>(() => _client.ReadAsync<Parameters>(url));

            // Assert
            Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        }

        [RequiresIsolatedDatabase]
        public class StartupForJobStatus : StartupBaseForCustomProviders
        {
            public StartupForJobStatus(IConfiguration configuration)
                : base(new ConfigurationBuilder()
                    .AddConfiguration(configuration)
                    .AddInMemoryCollection(new Dictionary<string, string>
                    {
                        ["FhirServer:Operations:JobStatus:PageSize"] = "3",
                    })
                    .Build())
            {
            }
        }
    }
}
