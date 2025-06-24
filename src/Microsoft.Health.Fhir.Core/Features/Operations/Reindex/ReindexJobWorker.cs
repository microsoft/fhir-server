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
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
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
        private readonly IScopeProvider<IFhirOperationDataStore> _fhirOperationDataStoreFactory;
        private readonly ReindexJobConfiguration _reindexJobConfiguration;
        private readonly IScopeProvider<IReindexJobTask> _reindexJobTaskFactory;
        private readonly ISearchParameterOperations _searchParameterOperations;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly ILogger _logger;
        private bool _searchParametersInitialized = false;

        public ReindexJobWorker(
            IScopeProvider<IFhirOperationDataStore> fhirOperationDataStoreFactory,
            IOptions<ReindexJobConfiguration> reindexJobConfiguration,
            IScopeProvider<IReindexJobTask> reindexJobTaskFactory,
            ISearchParameterOperations searchParameterOperations,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            ILogger<ReindexJobWorker> logger)
        {
            _fhirOperationDataStoreFactory = EnsureArg.IsNotNull(fhirOperationDataStoreFactory, nameof(fhirOperationDataStoreFactory));
            _reindexJobConfiguration = EnsureArg.IsNotNull(reindexJobConfiguration?.Value, nameof(reindexJobConfiguration));
            _reindexJobTaskFactory = EnsureArg.IsNotNull(reindexJobTaskFactory, nameof(reindexJobTaskFactory));
            _searchParameterOperations = EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            _contextAccessor = EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            var runningTasks = new List<(Task Task, IScoped<IReindexJobTask> Scope)>();

            while (!cancellationToken.IsCancellationRequested)
            {
                if (_searchParametersInitialized)
                {
                    var originalRequestContext = _contextAccessor.RequestContext;

                    // Create a background task context to trigger the correct retry policy.
                    var fhirRequestContext = new FhirRequestContext(
                        method: nameof(ReindexJobWorker),
                        uriString: nameof(ReindexJobWorker),
                        baseUriString: nameof(ReindexJobWorker),
                        correlationId: Guid.NewGuid().ToString(),
                        requestHeaders: new Dictionary<string, StringValues>(),
                        responseHeaders: new Dictionary<string, StringValues>())
                        {
                            IsBackgroundTask = true,
                        };

                    _contextAccessor.RequestContext = fhirRequestContext;

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
                        _logger.LogInformation("Number of Tasks in the list {RunningTasksCount}.", runningTasks.Count);

                        // Remove all completed tasks.
                        foreach (var task in runningTasks.Where(task => task.Task.IsCompleted).ToList())
                        {
                            task.Scope.Dispose();
                            runningTasks.Remove(task);
                        }

                        _logger.LogInformation("Number of running Tasks after removing completed tasks {RunningTasksCount}.", runningTasks.Count);

                        // Get list of available jobs.
                        if (runningTasks.Count < _reindexJobConfiguration.MaximumNumberOfConcurrentJobsAllowed)
                        {
                            using IScoped<IFhirOperationDataStore> store = _fhirOperationDataStoreFactory.Invoke();

                            _logger.LogInformation("Querying datastore for reindex jobs.");

                            IReadOnlyCollection<ReindexJobWrapper> jobs = await store.Value.AcquireReindexJobsAsync(
                                _reindexJobConfiguration.MaximumNumberOfConcurrentJobsAllowed,
                                _reindexJobConfiguration.JobHeartbeatTimeoutThreshold,
                                cancellationToken);

                            _logger.LogInformation("No.of reindex jobs picked.{JobsPicked} ", jobs.Count);

                            foreach (ReindexJobWrapper job in jobs)
                            {
                                _logger.LogInformation("Picked up reindex job: {JobId}.", job.JobRecord.Id);

                                IScoped<IReindexJobTask> reindexJobScope = _reindexJobTaskFactory.Invoke();
                                runningTasks.Add(
                                    (reindexJobScope.Value.ExecuteAsync(job.JobRecord, job.ETag, cancellationToken),
                                        reindexJobScope));
                            }
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // End the execution of the task
                        _logger.LogDebug("Polling Reindex jobs canceled.");
                    }
                    catch (Exception ex)
                    {
                        // The job failed.
                        _logger.LogError(ex, "Error polling Reindex jobs.");
                    }

                    _contextAccessor.RequestContext = originalRequestContext;
                }

                try
                {
                    await Task.Delay(_reindexJobConfiguration.JobPollingFrequency, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // End the execution of the task
                    _logger.LogDebug("Reindex job worker canceled.");
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
