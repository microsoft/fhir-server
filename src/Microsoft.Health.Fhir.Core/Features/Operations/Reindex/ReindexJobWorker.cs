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
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Messages.Search;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    /// <summary>
    /// The worker responsible for running the reindex job tasks.
    /// </summary>
    public class ReindexJobWorker : INotificationHandler<SearchParametersInitializedNotification>
    {
        private readonly Func<IScoped<IFhirOperationDataStore>> _fhirOperationDataStoreFactory;
        private readonly ReindexJobConfiguration _reindexJobConfiguration;
        private readonly Func<IReindexJobTask> _reindexJobTaskFactory;
        private readonly ISearchParameterOperations _searchParameterOperations;
        private readonly ILogger _logger;
        private bool _searchParametersInitialized = false;

        public ReindexJobWorker(
            Func<IScoped<IFhirOperationDataStore>> fhirOperationDataStoreFactory,
            IOptions<ReindexJobConfiguration> reindexJobConfiguration,
            Func<IReindexJobTask> reindexJobTaskFactory,
            ISearchParameterOperations searchParameterOperations,
            ILogger<ReindexJobWorker> logger)
        {
            EnsureArg.IsNotNull(fhirOperationDataStoreFactory, nameof(fhirOperationDataStoreFactory));
            EnsureArg.IsNotNull(reindexJobConfiguration?.Value, nameof(reindexJobConfiguration));
            EnsureArg.IsNotNull(reindexJobTaskFactory, nameof(reindexJobTaskFactory));
            EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirOperationDataStoreFactory = fhirOperationDataStoreFactory;
            _reindexJobConfiguration = reindexJobConfiguration.Value;
            _reindexJobTaskFactory = reindexJobTaskFactory;
            _searchParameterOperations = searchParameterOperations;
            _logger = logger;
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            var runningTasks = new List<Task>();

            while (!cancellationToken.IsCancellationRequested)
            {
                if (_searchParametersInitialized)
                {
                    // Check for any changes to Search Parameters
                    try
                    {
                        await _searchParameterOperations.GetAndApplySearchParameterUpdates(cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogDebug("Reindex job worker canceled.");
                    }
                    catch (Exception ex)
                    {
                        // The job failed.
                        _logger.LogError(ex, "Error querying latest SearchParameterStatus updates");
                    }

                    // Check for any new Reindex Jobs
                    try
                    {
                        // Remove all completed tasks.
                        runningTasks.RemoveAll(task => task.IsCompleted);

                        // Get list of available jobs.
                        if (runningTasks.Count < _reindexJobConfiguration.MaximumNumberOfConcurrentJobsAllowed)
                        {
                            using (IScoped<IFhirOperationDataStore> store = _fhirOperationDataStoreFactory.Invoke())
                            {
                                _logger.LogTrace("Querying datastore for reindex jobs.");

                                IReadOnlyCollection<ReindexJobWrapper> jobs = await store.Value.AcquireReindexJobsAsync(
                                    _reindexJobConfiguration.MaximumNumberOfConcurrentJobsAllowed,
                                    _reindexJobConfiguration.JobHeartbeatTimeoutThreshold,
                                    cancellationToken);

                                foreach (ReindexJobWrapper job in jobs)
                                {
                                    _logger.LogTrace("Picked up reindex job: {JobId}.", job.JobRecord.Id);

                                    runningTasks.Add(_reindexJobTaskFactory().ExecuteAsync(job.JobRecord, job.ETag, cancellationToken));
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // End the execution of the task
                    }
                    catch (Exception ex)
                    {
                        // The job failed.
                        _logger.LogError(ex, "Error polling Reindex jobs.");
                    }
                }

                try
                {
                    await Task.Delay(_reindexJobConfiguration.JobPollingFrequency, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // End the execution of the task
                }
            }
        }

        public Task Handle(SearchParametersInitializedNotification notification, CancellationToken cancellationToken)
        {
            _logger.LogInformation("ReindexJobWorker: Search parameters initialized");
            _searchParametersInitialized = true;
            return Task.CompletedTask;
        }
    }
}
