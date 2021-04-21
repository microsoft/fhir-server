// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.TaskManagement;

namespace Microsoft.Health.Fhir.Api.Features.BackgroundTaskService
{
    /// <summary>
    /// The background service used to host the <see cref="TaskHosting"/>.
    /// </summary>
    public class TaskHostingBackgroundService : BackgroundService
    {
        private readonly Func<IScoped<TaskHosting>> _taskHostingFactory;

        public TaskHostingBackgroundService(Func<IScoped<TaskHosting>> taskHostingFactory)
        {
            EnsureArg.IsNotNull(taskHostingFactory, nameof(taskHostingFactory));

            _taskHostingFactory = taskHostingFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using (IScoped<TaskHosting> taskHosting = _taskHostingFactory())
            {
                await taskHosting.Value.StartAsync(CancellationTokenSource.CreateLinkedTokenSource(stoppingToken));
            }
        }
    }
}
