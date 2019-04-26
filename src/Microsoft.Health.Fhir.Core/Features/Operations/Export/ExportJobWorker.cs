// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    /// <summary>
    /// The worker responsible for running the export job tasks.
    /// </summary>
    public class ExportJobWorker
    {
        private readonly IFhirOperationsDataStore _fhirOperationsDataStore;
        private readonly ExportJobConfiguration _exportJobConfiguration;
        private readonly IExportJobTaskFactory _exportJobTaskFactory;
        private readonly ILogger _logger;

        public ExportJobWorker(IFhirOperationsDataStore fhirOperationsDataStore, IOptions<ExportJobConfiguration> exportJobConfiguration, IExportJobTaskFactory exportJobTaskFactory, ILogger<ExportJobWorker> logger)
        {
            EnsureArg.IsNotNull(fhirOperationsDataStore, nameof(fhirOperationsDataStore));
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));
            EnsureArg.IsNotNull(exportJobTaskFactory, nameof(exportJobTaskFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirOperationsDataStore = fhirOperationsDataStore;
            _exportJobConfiguration = exportJobConfiguration.Value;
            _exportJobTaskFactory = exportJobTaskFactory;
            _logger = logger;
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            var runningTasks = new List<Task>();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Remove all completed tasks.
                    runningTasks.RemoveAll(task => task.IsCompleted);

                    // Get list of available jobs.
                    if (runningTasks.Count < _exportJobConfiguration.MaximumNumberOfConcurrentJobsAllowed)
                    {
                        IReadOnlyCollection<ExportJobOutcome> jobs = await _fhirOperationsDataStore.GetAvailableExportJobsAsync(
                            _exportJobConfiguration.MaximumNumberOfConcurrentJobsAllowed,
                            _exportJobConfiguration.JobHeartbeatTimeoutThreshold,
                            cancellationToken);

                        runningTasks.AddRange(jobs.Select(job => _exportJobTaskFactory.Create(job.JobRecord, job.ETag, cancellationToken)));
                    }

                    await Task.Delay(_exportJobConfiguration.JobPollingFrequency);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
                {
                    // The job failed.
                    _logger.LogError(ex, "Unhandled exception in the worker.");
                }
            }
        }
    }
}
