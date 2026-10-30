// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Features.BackgroundJobService;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Scoping;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.BackgroundJobService;

[Trait(Traits.OwningTeam, OwningTeam.Fhir)]
[Trait(Traits.Category, Categories.Operations)]
public sealed class HostingBackgroundServiceTests
{
    [Fact]
    public void GivenOperationsConfigurationWithMixedEnabledQueues_WhenGetEnabledQueueConfigsIsCalled_ThenOnlyEnabledQueuesAreReturned()
    {
        // Arrange
        var exportConfig = new ExportJobConfiguration { Enabled = true };
        var importConfig = new ImportJobConfiguration { Enabled = false };
        var bulkDeleteConfig = new BulkDeleteJobConfiguration { Enabled = true };
        var bulkUpdateConfig = new BulkUpdateJobConfiguration { Enabled = true };

        var operationsConfig = new OperationsConfiguration
        {
            Export = exportConfig,
            Import = importConfig,
            BulkDelete = bulkDeleteConfig,
            BulkUpdate = bulkUpdateConfig,
        };

        var jobHostingFactory = Substitute.For<IScopeProvider<JobHosting>>();
        var hostingConfig = Options.Create(new TaskHostingConfiguration());
        var operationsConfigOptions = Options.Create(operationsConfig);
        var logger = Substitute.For<ILogger<HostingBackgroundService>>();

        var service = new HostingBackgroundService(jobHostingFactory, hostingConfig, operationsConfigOptions, logger);

        // Act
        var enabledQueues = service.GetEnabledQueueConfigs().ToList();

        // Assert
        Assert.Contains(enabledQueues, q => q == exportConfig);
        Assert.Contains(enabledQueues, q => q == bulkDeleteConfig);
        Assert.Contains(enabledQueues, q => q == bulkUpdateConfig);
        Assert.DoesNotContain(enabledQueues, q => q == importConfig);
        Assert.Equal(3, enabledQueues.Count);
    }

    [Fact]
    public async Task GivenQueueExecutionIsRunning_WhenServiceStops_ThenHostingScopeIsDisposedAfterQueueExecutionCompletes()
    {
        var dequeueStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queueClient = Substitute.For<IQueueClient>();
        queueClient.IsInitialized().Returns(true);
        queueClient.DequeueAsync(
                Arg.Any<byte>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<long?>(),
                Arg.Any<bool>())
            .Returns(call => DequeueUntilCancelledAsync(dequeueStarted, call.ArgAt<CancellationToken>(3)));

        var jobHosting = new JobHosting(
            queueClient,
            Substitute.For<IJobFactory>(),
            Substitute.For<IJobMetricFactory>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<JobHosting>.Instance);
        var hostingScope = new CountingHostingScope(jobHosting);
        var jobHostingFactory = Substitute.For<IScopeProvider<JobHosting>>();
        jobHostingFactory.Invoke().Returns(hostingScope);
        var operationsConfig = new OperationsConfiguration
        {
            Export = new ExportJobConfiguration { Enabled = true },
            Import = new ImportJobConfiguration { Enabled = false },
            BulkDelete = new BulkDeleteJobConfiguration { Enabled = false },
            BulkUpdate = new BulkUpdateJobConfiguration { Enabled = false },
        };
        using var service = new HostingBackgroundService(
            jobHostingFactory,
            Options.Create(new TaskHostingConfiguration { PollingFrequencyInSeconds = 0 }),
            Options.Create(operationsConfig),
            Substitute.For<ILogger<HostingBackgroundService>>());
        await service.HandleAsync(new SearchParametersInitializedNotification(), CancellationToken.None);

        try
        {
            await service.StartAsync(CancellationToken.None);
            await dequeueStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(0, hostingScope.DisposeCount);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        jobHostingFactory.Received(1).Invoke();
        Assert.Equal(1, hostingScope.DisposeCount);
    }

    private static async Task<JobInfo> DequeueUntilCancelledAsync(TaskCompletionSource dequeueStarted, CancellationToken cancellationToken)
    {
        dequeueStarted.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return null;
    }

    private sealed class CountingHostingScope : IScoped<JobHosting>
    {
        private int _disposeCount;

        public CountingHostingScope(JobHosting value)
        {
            Value = value;
        }

        public JobHosting Value { get; }

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
        }
    }
}
