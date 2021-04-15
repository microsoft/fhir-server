// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.TaskManagement;

namespace Microsoft.Health.Fhir.Api.Features.BackgroundTaskService
{
    /// <summary>
    /// The background service used to host the <see cref="TaskHosting"/>.
    /// </summary>
    public class TaskHostingBackgroundService : BackgroundService
    {
        private readonly Func<IScoped<TaskHosting>> _taskHostingFactory;
        private readonly TaskHostingConfiguration _taskHostingConfiguration;

        public TaskHostingBackgroundService(Func<IScoped<TaskHosting>> taskHostingFactory, IOptions<TaskHostingConfiguration> taskHostingConfiguration)
        {
            EnsureArg.IsNotNull(taskHostingFactory, nameof(taskHostingFactory));

            _taskHostingFactory = taskHostingFactory;
            _taskHostingConfiguration = taskHostingConfiguration.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using (IScoped<TaskHosting> taskHosting = _taskHostingFactory())
            {
                var taskHostingValue = taskHosting.Value;
                if (_taskHostingConfiguration != null)
                {
                    taskHostingValue.PollingFrequencyInSeconds = _taskHostingConfiguration.PollingFrequencyInSeconds ?? taskHostingValue.PollingFrequencyInSeconds;
                    taskHostingValue.MaxRunningTaskCount = _taskHostingConfiguration.MaxRunningTaskCount ?? taskHostingValue.MaxRunningTaskCount;
                    taskHostingValue.MaxRetryCount = _taskHostingConfiguration.MaxRetryCount ?? taskHostingValue.MaxRetryCount;
                    taskHostingValue.TaskHeartbeatIntervalInSeconds = _taskHostingConfiguration.TaskHeartbeatIntervalInSeconds ?? taskHostingValue.TaskHeartbeatIntervalInSeconds;
                    taskHostingValue.TaskHeartbeatTimeoutThresholdInSeconds = _taskHostingConfiguration.TaskHeartbeatTimeoutThresholdInSeconds ?? taskHostingValue.TaskHeartbeatTimeoutThresholdInSeconds;
                }

                await taskHostingValue.StartAsync(CancellationTokenSource.CreateLinkedTokenSource(stoppingToken));
            }
        }
    }
}
