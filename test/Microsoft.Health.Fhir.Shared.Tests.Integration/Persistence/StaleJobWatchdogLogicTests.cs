// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.StaleJob.Messages;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class StaleJobWatchdogLogicTests
    {
        private static DateTime _now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void ComputeQueueAges_WhenAnyJobRunning_EmitsZeroForAllQueues()
        {
            var jobs = new Dictionary<QueueType, IReadOnlyList<JobInfo>>
            {
                [QueueType.Export] = new List<JobInfo>
                {
                    new JobInfo { Status = JobStatus.Running, CreateDate = _now.AddMinutes(-15) },
                },
                [QueueType.Import] = new List<JobInfo>
                {
                    new JobInfo { Status = JobStatus.Created, CreateDate = _now.AddMinutes(-20) },
                },
            };

            var result = StaleJobWatchdog.ComputeQueueAges(jobs, _now);

            // Export has a running job -> 0. Import has no running job and a 20-minute-old created job -> 1200s.
            Assert.Equal(0, result[QueueType.Export]);
            Assert.Equal(20 * 60, result[QueueType.Import]);
        }

        [Fact]
        public void ComputeQueueAges_WhenNoJobsRunning_EmitsOldestCreatedJobAge()
        {
            var jobs = new Dictionary<QueueType, IReadOnlyList<JobInfo>>
            {
                [QueueType.Export] = new List<JobInfo>
                {
                    new JobInfo { Status = JobStatus.Created, CreateDate = _now.AddSeconds(-754) },
                    new JobInfo { Status = JobStatus.Created, CreateDate = _now.AddSeconds(-200) },
                },
                [QueueType.Import] = new List<JobInfo>(),
            };

            var result = StaleJobWatchdog.ComputeQueueAges(jobs, _now);

            Assert.Equal(754.0, result[QueueType.Export]);
            Assert.Equal(0, result[QueueType.Import]);
        }

        [Fact]
        public void ComputeQueueAges_WhenQueueEmpty_EmitsZero()
        {
            var jobs = new Dictionary<QueueType, IReadOnlyList<JobInfo>>
            {
                [QueueType.Export] = new List<JobInfo>(),
            };

            var result = StaleJobWatchdog.ComputeQueueAges(jobs, _now);

            Assert.Equal(0, result[QueueType.Export]);
        }

        [Fact]
        public void ComputeQueueAges_IsPerQueue_NotGlobal()
        {
            // A running job in one queue must NOT mask staleness in another queue.
            var jobs = new Dictionary<QueueType, IReadOnlyList<JobInfo>>
            {
                [QueueType.Export] = new List<JobInfo>
                {
                    new JobInfo { Status = JobStatus.Running, CreateDate = _now.AddMinutes(-1) },
                },
                [QueueType.Import] = new List<JobInfo>
                {
                    new JobInfo { Status = JobStatus.Created, CreateDate = _now.AddSeconds(-900) },
                },
            };

            var result = StaleJobWatchdog.ComputeQueueAges(jobs, _now);

            Assert.Equal(0, result[QueueType.Export]);
            Assert.Equal(900, result[QueueType.Import]);
        }

        [Fact]
        public async Task StaleJobMetricHandler_Handle_UpdatesQueueAges()
        {
            var services = new ServiceCollection();
            services.AddMetrics();
            var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IMeterFactory>();
            var handler = new StaleJobMetricHandler(factory);

            var ages = new Dictionary<QueueType, double>
            {
                [QueueType.Export] = 754.0,
                [QueueType.Import] = 0.0,
            };

            await handler.Handle(new StaleJobMetricsNotification(ages), CancellationToken.None);

            Assert.Equal(754.0, handler.QueueAges[QueueType.Export]);
            Assert.Equal(0.0, handler.QueueAges[QueueType.Import]);
        }

        [Fact]
        public async Task StaleJobMetricHandler_Handle_OverwritesPreviousValues()
        {
            var services = new ServiceCollection();
            services.AddMetrics();
            var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<IMeterFactory>();
            var handler = new StaleJobMetricHandler(factory);

            await handler.Handle(
                new StaleJobMetricsNotification(new Dictionary<QueueType, double> { [QueueType.Export] = 500.0 }),
                CancellationToken.None);

            await handler.Handle(
                new StaleJobMetricsNotification(new Dictionary<QueueType, double> { [QueueType.Export] = 0.0 }),
                CancellationToken.None);

            Assert.Equal(0.0, handler.QueueAges[QueueType.Export]);
        }
    }
}
