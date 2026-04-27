// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Watchdogs
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class ExpiredResourceCleanupWatchdogTests
    {
        private readonly ExpiredResourceCleanupWatchdog _watchdog;
        private readonly ISqlRetryService _sqlRetryService;
        private readonly IQueueClient _queueClient;
        private readonly ILogger<ExpiredResourceCleanupWatchdog> _logger;

        public ExpiredResourceCleanupWatchdogTests()
        {
            _sqlRetryService = Substitute.For<ISqlRetryService>();
            _queueClient = Substitute.For<IQueueClient>();
            _logger = NullLogger<ExpiredResourceCleanupWatchdog>.Instance;

            var configuration = new WatchdogConfiguration();
            configuration.ExpiredResource.Enabled = true;

            var watchdogOptions = Options.Create(configuration);

            _watchdog = new ExpiredResourceCleanupWatchdog(
                _sqlRetryService,
                _queueClient,
                watchdogOptions,
                _logger);
        }

        [Fact]
        public void GivenAWatchdog_WhenCheckingDefaultValues_ThenCorrectDefaultsAreSet()
        {
            // Arrange
            var watchdog = new ExpiredResourceCleanupWatchdog();

            // Assert
            Assert.Equal(4 * 3600, watchdog.PeriodSec);
            Assert.Equal(3600, watchdog.LeasePeriodSec);
            Assert.False(watchdog.AllowRebalance);
        }

        [Fact]
        public async Task GivenWatchdogEnabled_WhenRunWorkAsyncIsCalled_ThenBulkDeleteJobIsEnqueued()
        {
            // Arrange
            var expectedJobInfo = new JobInfo { Id = 123 };

            // Mock the underlying IQueueClient.EnqueueAsync that the extension method calls
            _queueClient.EnqueueAsync(
                Arg.Is<byte>(q => q == (byte)QueueType.BulkDelete),
                Arg.Any<string[]>(),
                Arg.Any<long?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
                .Returns(new List<JobInfo> { expectedJobInfo });

            // Act
            await _watchdog.RunWorkForTestingAsync(CancellationToken.None);

            // Assert - verify that EnqueueAsync was called with the correct queue type
            await _queueClient.Received(1).EnqueueAsync(
                Arg.Is<byte>(q => q == (byte)QueueType.BulkDelete),
                Arg.Is<string[]>(defs => defs.Length == 1 && VerifyBulkDeleteDefinition(defs[0])),
                Arg.Any<long?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenWatchdogDisabled_WhenRunWorkAsyncIsCalled_ThenBulkDeleteJobIsNotEnqueued()
        {
            // Arrange
            var configuration = new WatchdogConfiguration();
            configuration.ExpiredResource.Enabled = false;

            var watchdogOptions = Options.Create(configuration);

            var watchdog = new ExpiredResourceCleanupWatchdog(
                _sqlRetryService,
                _queueClient,
                watchdogOptions,
                _logger);

            // Act
            await watchdog.RunWorkForTestingAsync(CancellationToken.None);

            // Assert
            await _queueClient.DidNotReceive().EnqueueAsync(
                Arg.Any<byte>(),
                Arg.Any<string[]>(),
                Arg.Any<long?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenWatchdogEnabled_WhenEnqueueFails_ThenNoExceptionIsThrown()
        {
            _queueClient.EnqueueAsync(
                Arg.Any<byte>(),
                Arg.Any<string[]>(),
                Arg.Any<long?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
                .Returns(Task.FromException<IReadOnlyList<JobInfo>>(new Exception("Enqueue failed")));

            // Act & Assert - should not throw
            await _watchdog.RunWorkForTestingAsync(CancellationToken.None);
        }

        [Fact]
        public async Task GivenWatchdogEnabled_WhenEnqueueReturnsNull_ThenNoExceptionIsThrown()
        {
            _queueClient.EnqueueAsync(
                Arg.Any<byte>(),
                Arg.Any<string[]>(),
                Arg.Any<long?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
                .Returns((IReadOnlyList<JobInfo>)null);

            // Act & Assert - should not throw
            await _watchdog.RunWorkForTestingAsync(CancellationToken.None);
        }

        private static bool VerifyBulkDeleteDefinition(string definitionJson)
        {
            var definition = JsonConvert.DeserializeObject<BulkDeleteDefinition>(definitionJson);
            return definition != null &&
                   definition.TypeId == (int)JobType.BulkDeleteOrchestrator &&
                   definition.DeleteOperation == DeleteOperation.HardDelete;
        }
    }
}
