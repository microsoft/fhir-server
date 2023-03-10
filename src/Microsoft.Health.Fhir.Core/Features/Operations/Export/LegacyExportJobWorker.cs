// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Messages.Storage;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    /// <summary>
    /// The worker responsible for running the export job tasks.
    /// </summary>
    public class LegacyExportJobWorker : INotificationHandler<StorageInitializedNotification>
    {
        private readonly Func<IScoped<ILegacyExportOperationDataStore>> _fhirOperationDataStoreFactory;
        private readonly ExportJobConfiguration _exportJobConfiguration;
        private readonly Func<IExportJobTask> _exportJobTaskFactory;
        private readonly ILogger _logger;
        private bool _storageReady;

        private const int MaximumDelayInSeconds = 3600;

        public LegacyExportJobWorker(Func<IScoped<ILegacyExportOperationDataStore>> fhirOperationDataStoreFactory, IOptions<ExportJobConfiguration> exportJobConfiguration, Func<IExportJobTask> exportJobTaskFactory, ILogger<LegacyExportJobWorker> logger)
        {
            EnsureArg.IsNotNull(fhirOperationDataStoreFactory, nameof(fhirOperationDataStoreFactory));
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));
            EnsureArg.IsNotNull(exportJobTaskFactory, nameof(exportJobTaskFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirOperationDataStoreFactory = fhirOperationDataStoreFactory;
            _exportJobConfiguration = exportJobConfiguration.Value;
            _exportJobTaskFactory = exportJobTaskFactory;
            _logger = logger;
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            var runningTasks = new List<Task>();
            TimeSpan delayBeforeNextPoll = _exportJobConfiguration.JobPollingFrequency;

            while (!cancellationToken.IsCancellationRequested)
            {
                if (_storageReady)
                {
                    try
                    {
                        // Remove all completed tasks.
                        runningTasks.RemoveAll(task => task.IsCompleted);

                        // Get list of available jobs.
                        if (runningTasks.Count < _exportJobConfiguration.MaximumNumberOfConcurrentJobsAllowedPerInstance)
                        {
                            using IScoped<ILegacyExportOperationDataStore> store = _fhirOperationDataStoreFactory();
                            ushort numberOfJobsToAcquire = (ushort)(_exportJobConfiguration.MaximumNumberOfConcurrentJobsAllowedPerInstance - runningTasks.Count);

                            IReadOnlyCollection<ExportJobOutcome> jobs = await store.Value.AcquireExportJobsAsync(
                                numberOfJobsToAcquire,
                                _exportJobConfiguration.JobHeartbeatTimeoutThreshold,
                                cancellationToken);

                            foreach (ExportJobOutcome job in jobs)
                            {
                                _logger.LogTrace("Picked up job: {JobId}.", job.JobRecord.Id);

                                runningTasks.Add(_exportJobTaskFactory().ExecuteAsync(job.JobRecord, job.ETag, cancellationToken));
                            }
                        }

                        // We successfully completed an attempt to acquire export jobs. Let us reset the polling frequency in case it has changed.
                        delayBeforeNextPoll = _exportJobConfiguration.JobPollingFrequency;
                    }
                    catch (Exception ex)
                    {
                        // The job failed.
                        _logger.LogError(ex, "Unhandled exception in the worker.");

                        // Since acquiring jobs failed let us introduce a delay before we retry. We don't want to increase the delay between polls to more than an hour.
                        delayBeforeNextPoll *= 2;
                        if (delayBeforeNextPoll.TotalSeconds > MaximumDelayInSeconds)
                        {
                            delayBeforeNextPoll = TimeSpan.FromSeconds(MaximumDelayInSeconds);
                        }
                    }
                }

                try
                {
                    await Task.Delay(delayBeforeNextPoll, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Cancellation requested
                    break;
                }
            }

            _logger.LogInformation("ExportJobWorker: Cancellation requested.");
        }

        public Task Handle(StorageInitializedNotification notification, CancellationToken cancellationToken)
        {
            _logger.LogInformation("ExportJobWorker: Storage initialized");
            _storageReady = true;
            return Task.CompletedTask;
        }
    }
}
