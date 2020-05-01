// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    /// <summary>
    /// The worker responsible for running the export job tasks.
    /// </summary>
    public class ExportJobWorker
    {
        private readonly Func<IScoped<IFhirOperationDataStore>> _fhirOperationDataStoreFactory;
        private readonly ExportJobConfiguration _exportJobConfiguration;
        private readonly Func<IExportJobTask> _exportJobTaskFactory;
        private readonly ILogger _logger;

        private TimeSpan _delayBeforeNextPoll;

        public ExportJobWorker(Func<IScoped<IFhirOperationDataStore>> fhirOperationDataStoreFactory, IOptions<ExportJobConfiguration> exportJobConfiguration, Func<IExportJobTask> exportJobTaskFactory, ILogger<ExportJobWorker> logger)
        {
            EnsureArg.IsNotNull(fhirOperationDataStoreFactory, nameof(fhirOperationDataStoreFactory));
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));
            EnsureArg.IsNotNull(exportJobTaskFactory, nameof(exportJobTaskFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirOperationDataStoreFactory = fhirOperationDataStoreFactory;
            _exportJobConfiguration = exportJobConfiguration.Value;
            _exportJobTaskFactory = exportJobTaskFactory;
            _logger = logger;

            _delayBeforeNextPoll = _exportJobConfiguration.JobPollingFrequency;
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
                        using (IScoped<IFhirOperationDataStore> store = _fhirOperationDataStoreFactory())
                        {
                            IReadOnlyCollection<ExportJobOutcome> jobs = await store.Value.AcquireExportJobsAsync(
                                _exportJobConfiguration.MaximumNumberOfConcurrentJobsAllowed,
                                _exportJobConfiguration.JobHeartbeatTimeoutThreshold,
                                cancellationToken);

                            foreach (ExportJobOutcome job in jobs)
                            {
                                _logger.LogTrace($"Picked up job: {job.JobRecord.Id}.");

                                runningTasks.Add(_exportJobTaskFactory().ExecuteAsync(job.JobRecord, job.ETag, cancellationToken));
                            }
                        }
                    }

                    // We successfully completed an attempt to acquire export jobs. Let us reset the polling frquency in case it has changed.
                    _delayBeforeNextPoll = _exportJobConfiguration.JobPollingFrequency;
                }
                catch (Exception ex)
                {
                    // The job failed.
                    _logger.LogError(ex, "Unhandled exception in the worker.");

                    // Since acquiring jobs failed let us introduce a delay before we retry. We don't want to increase the delay between polls to more than an hour.
                    _delayBeforeNextPoll *= 2;
                    if (_delayBeforeNextPoll > TimeSpan.FromSeconds(3600))
                    {
                        _delayBeforeNextPoll = TimeSpan.FromSeconds(3600);
                    }
                }

                try
                {
                    await Task.Delay(_delayBeforeNextPoll, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Cancellation requested
                    _logger.LogInformation("ExportJobWorker: Cancellation requested.");
                }
            }
        }
    }
}
