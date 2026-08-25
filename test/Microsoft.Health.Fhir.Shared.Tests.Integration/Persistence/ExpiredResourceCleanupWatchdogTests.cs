// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Integration tests for the ExpiredResourceCleanupWatchdog.
    /// </summary>
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class ExpiredResourceCleanupWatchdogTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private readonly SqlServerFhirStorageTestsFixture _fixture;
        private readonly ITestOutputHelper _testOutputHelper;
        private readonly IQueueClient _queueClient;
        private readonly ISqlRetryService _sqlRetryService;

        public ExpiredResourceCleanupWatchdogTests(SqlServerFhirStorageTestsFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _fixture = fixture;
            _testOutputHelper = testOutputHelper;
            _queueClient = (IQueueClient)((System.IServiceProvider)_fixture).GetService(typeof(IQueueClient));
            _sqlRetryService = _fixture.SqlRetryService;
        }

        [Fact]
        public async Task GivenWatchdogEnabled_WhenRunWorkAsync_ThenBulkDeleteJobIsEnqueued()
        {
            // Arrange
            var configuration = new WatchdogConfiguration();
            configuration.ExpiredResource.Enabled = true;

            var watchdogOptions = Options.Create(configuration);

            var watchdog = new ExpiredResourceCleanupWatchdog(
                _sqlRetryService,
                _queueClient,
                watchdogOptions,
                NullLogger<ExpiredResourceCleanupWatchdog>.Instance);

            // Act
            await watchdog.RunWorkForTestingAsync(CancellationToken.None);

            // Assert - Dequeue the job to verify it was created
            var job = await _queueClient.DequeueAsync(
                (byte)QueueType.BulkDelete,
                worker: "IntegrationTest",
                heartbeatTimeoutSec: 600,
                CancellationToken.None);

            Assert.NotNull(job);
            Assert.NotNull(job.Definition);

            var definition = JsonConvert.DeserializeObject<BulkDeleteDefinition>(job.Definition);
            Assert.NotNull(definition);
            Assert.Equal((int)JobType.BulkDeleteOrchestrator, definition.TypeId);
            Assert.Equal(DeleteOperation.HardDelete, definition.DeleteOperation);

            var removeReferencesParam = definition.SearchParameters.FirstOrDefault(p => p.Item1 == KnownQueryParameterNames.RemoveReferences);
            Assert.NotNull(removeReferencesParam);
            Assert.Equal("true", removeReferencesParam.Item2);

            var expiryDateParam = definition.SearchParameters.FirstOrDefault(p => p.Item1 == "_expiryDate");
            Assert.NotNull(expiryDateParam);
            Assert.StartsWith("lt", expiryDateParam.Item2);

            _testOutputHelper.WriteLine($"Successfully verified bulk delete job {job.Id} was enqueued.");

            // Clean up - cancel the job so it doesn't interfere with other tests
            await _queueClient.CancelJobByIdAsync((byte)QueueType.BulkDelete, job.Id, CancellationToken.None);
        }
    }
}
