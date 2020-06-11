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
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    /// <summary>
    /// The worker responsible for running the reindex job tasks.
    /// </summary>
    public class ReindexJobWorker
    {
        private readonly Func<IScoped<IFhirOperationDataStore>> _fhirOperationDataStoreFactory;
        private readonly ReindexJobConfiguration _reindexJobConfiguration;
        private readonly Func<IReindexJobTask> _reindexJobTaskFactory;
        private readonly ILogger _logger;

        public ReindexJobWorker(Func<IScoped<IFhirOperationDataStore>> fhirOperationDataStoreFactory, IOptions<ReindexJobConfiguration> reindexJobConfiguration, Func<IReindexJobTask> reindexJobTaskFactory, ILogger<ReindexJobWorker> logger)
        {
            EnsureArg.IsNotNull(fhirOperationDataStoreFactory, nameof(fhirOperationDataStoreFactory));
            EnsureArg.IsNotNull(reindexJobConfiguration?.Value, nameof(reindexJobConfiguration));
            EnsureArg.IsNotNull(reindexJobTaskFactory, nameof(reindexJobTaskFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirOperationDataStoreFactory = fhirOperationDataStoreFactory;
            _reindexJobConfiguration = reindexJobConfiguration.Value;
            _reindexJobTaskFactory = reindexJobTaskFactory;
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
                    if (runningTasks.Count < _reindexJobConfiguration.MaximumNumberOfConcurrentJobsAllowed)
                    {
                        using (IScoped<IFhirOperationDataStore> store = _fhirOperationDataStoreFactory())
                        {
                            _logger.LogTrace("Querying datastore for reindex jobs.");

                            IReadOnlyCollection<ReindexJobWrapper> jobs = await store.Value.AcquireReindexJobsAsync(
                                _reindexJobConfiguration.MaximumNumberOfConcurrentJobsAllowed,
                                _reindexJobConfiguration.JobHeartbeatTimeoutThreshold,
                                cancellationToken);

                            foreach (ReindexJobWrapper job in jobs)
                            {
                                _logger.LogTrace($"Picked up reindex job: {job.JobRecord.Id}.");

                                runningTasks.Add(_reindexJobTaskFactory().ExecuteAsync(job.JobRecord, job.ETag, cancellationToken));
                            }
                        }
                    }

                    await Task.Delay(_reindexJobConfiguration.JobPollingFrequency, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // End the execution of the task
                }
                catch (Exception ex)
                {
                    // The job failed.
                    _logger.LogError(ex, "Unhandled exception in the worker.");
                    await Task.Delay(_reindexJobConfiguration.JobPollingFrequency, cancellationToken);
                }
            }
        }
    }
}
