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
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Messages.Storage;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    /// <summary>
    /// The worker responsible for running the export job tasks.
    /// </summary>
    public class ExportJobWorker : INotificationHandler<StorageInitializedNotification>
    {
        private readonly IBackgroundScopeProvider<IFhirOperationDataStore> _fhirOperationDataStoreFactory;
        private readonly ExportJobConfiguration _exportJobConfiguration;
        private readonly IBackgroundScopeProvider<IExportJobTask> _exportJobTaskFactory;
        private readonly ILogger _logger;
        private bool _storageReady;

        private const int MaximumDelayInSeconds = 3600;

        public ExportJobWorker(IBackgroundScopeProvider<IFhirOperationDataStore> fhirOperationDataStoreFactory, IOptions<ExportJobConfiguration> exportJobConfiguration, IBackgroundScopeProvider<IExportJobTask> exportJobTaskFactory, ILogger<ExportJobWorker> logger)
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
            var runningTasks = new List<(Task Task, IScoped<IExportJobTask> Scope)>();
            TimeSpan delayBeforeNextPoll = _exportJobConfiguration.JobPollingFrequency;

            while (!cancellationToken.IsCancellationRequested)
            {
                if (_storageReady)
                {
                    try
                    {
                        // Remove all completed tasks.
                        foreach (var task in runningTasks.Where(task => task.Task.IsCompleted).ToList())
                        {
                            task.Scope.Dispose();
                            runningTasks.Remove(task);
                        }

                        // Get list of available jobs.
                        if (runningTasks.Count < _exportJobConfiguration.MaximumNumberOfConcurrentJobsAllowedPerInstance)
                        {
                            using IScoped<IFhirOperationDataStore> store = _fhirOperationDataStoreFactory.Invoke();
                            ushort numberOfJobsToAcquire = (ushort)(_exportJobConfiguration.MaximumNumberOfConcurrentJobsAllowedPerInstance - runningTasks.Count);
                            IReadOnlyCollection<ExportJobOutcome> jobs = await store.Value.AcquireExportJobsAsync(
                                numberOfJobsToAcquire,
                                _exportJobConfiguration.JobHeartbeatTimeoutThreshold,
                                cancellationToken);

                            foreach (ExportJobOutcome job in jobs)
                            {
                                _logger.LogTrace("Picked up job: {JobId}.", job.JobRecord.Id);

                                IScoped<IExportJobTask> taskScope = _exportJobTaskFactory.Invoke();
                                runningTasks.Add((taskScope.Value.ExecuteAsync(job.JobRecord, job.ETag, cancellationToken), taskScope));
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
