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

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkImport
{
    /// <summary>
    /// The background service used to host the <see cref="TaskHosting"/>.
    /// </summary>
    public class BulkImportTaskBackgroundService : BackgroundService
    {
        private readonly Func<IScoped<TaskHosting>> _taskHostingFactory;
        private readonly BulkImportTaskConfiguration _bulkImportTaskbConfiguration;

        public BulkImportTaskBackgroundService(Func<IScoped<TaskHosting>> taskHostingFactory, IOptions<BulkImportTaskConfiguration> bulkImportTaskbConfiguration)
        {
            EnsureArg.IsNotNull(taskHostingFactory, nameof(taskHostingFactory));
            EnsureArg.IsNotNull(bulkImportTaskbConfiguration?.Value, nameof(bulkImportTaskbConfiguration));

            _taskHostingFactory = taskHostingFactory;
            _bulkImportTaskbConfiguration = bulkImportTaskbConfiguration.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_bulkImportTaskbConfiguration.Enabled)
            {
                using (IScoped<TaskHosting> taskHosting = _taskHostingFactory())
                {
                    await taskHosting.Value.StartAsync(CancellationTokenSource.CreateLinkedTokenSource(stoppingToken));
                }
            }
        }
    }
}
