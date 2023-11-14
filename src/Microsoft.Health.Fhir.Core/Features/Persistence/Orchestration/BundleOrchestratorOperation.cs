// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration
{
    public sealed class BundleOrchestratorOperation : IBundleOrchestratorOperation
    {
        private const int DelayTimeInMilliseconds = 10;

        private static readonly BundleResourceContextComparer _contextComparer = new BundleResourceContextComparer();

        /// <summary>
        /// List of resource to be sent to the data layer.
        /// </summary>
        private readonly ConcurrentDictionary<DataStoreOperationIdentifier, ResourceWrapperOperation> _resources;

        /// <summary>
        /// List of known HTTP Verbs in the operation.
        /// </summary>
        private readonly ConcurrentDictionary<HTTPVerb, byte> _knownHttpVerbsInOperation;

        /// <summary>
        /// Thread safe locking object reference.
        /// </summary>
        private readonly object _lock;

        /// <summary>
        /// Expected number of resources to be persisted.
        /// </summary>
        private int _currentExpectedNumberOfResources;

        /// <summary>
        /// Merge async task, only assigned and executed once.
        /// </summary>
        private Task<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>> _mergeAsyncTask;

        /// <summary>
        /// Current instance of data store in use.
        /// </summary>
        private IFhirDataStore _dataStore;

        /// <summary>
        /// Logger.
        /// </summary>
        private readonly ILogger<BundleOrchestrator> _logger;

        public BundleOrchestratorOperation(BundleOrchestratorOperationType type, string label, int expectedNumberOfResources, ILogger<BundleOrchestrator> logger)
        {
            EnsureArg.IsNotNullOrWhiteSpace(label, nameof(label));
            EnsureArg.IsGt(expectedNumberOfResources, 0, nameof(expectedNumberOfResources));
            EnsureArg.IsNotNull(logger, nameof(logger));

            Id = Guid.NewGuid();
            Type = type;
            Label = label;
            CreationTime = DateTime.UtcNow;
            Status = BundleOrchestratorOperationStatus.Open;

            OriginalExpectedNumberOfResources = expectedNumberOfResources;
            _currentExpectedNumberOfResources = expectedNumberOfResources;
            _logger = logger;

            _resources = new ConcurrentDictionary<DataStoreOperationIdentifier, ResourceWrapperOperation>();
            _knownHttpVerbsInOperation = new ConcurrentDictionary<HTTPVerb, byte>();

            _lock = new object();

            _mergeAsyncTask = null;
            _dataStore = null;
        }

        public Guid Id { get; private set; }

        public BundleOrchestratorOperationType Type { get; private set; }

        public string Label { get; private set; }

        /// <summary>
        /// Original expected number of resources when the job was created..
        /// </summary>
        public int OriginalExpectedNumberOfResources { get; private set; }

        /// <summary>
        /// Current expected number of resources. This number can be different thatn the original expected number of resources,
        /// if a conditional validation rejected a resource to be persisted.
        /// </summary>
        public int CurrentExpectedNumberOfResources => _currentExpectedNumberOfResources;

        public DateTime CreationTime { get; private set; }

        public BundleOrchestratorOperationStatus Status { get; private set; }

        public async Task<UpsertOutcome> AppendResourceAsync(ResourceWrapperOperation resource, IFhirDataStore dataStore, CancellationToken cancellationToken)
        {
            DataStoreOperationIdentifier identifier = null;
            try
            {
                if (!_resources.Any())
                {
                    SetStatusSafe(BundleOrchestratorOperationStatus.WaitingForResources);
                }

                InitializeMergeTaskSafe(dataStore, cancellationToken);
            }
            catch (Exception ex)
            {
                SetStatusSafe(BundleOrchestratorOperationStatus.Failed);

                _logger.LogError(ex, "Bundle Operation {Id}. Failed while appending a new resource in a Bundle Operation: {ErrorMessage}", Id, ex.Message);
                throw;
            }

            identifier = resource.GetIdentifier();
            if (_resources.TryAdd(identifier, resource))
            {
                _knownHttpVerbsInOperation.TryAdd(resource.BundleResourceContext.HttpVerb, 0);

                // Await for the merge async task to complete merging all resources.
                var ingestedResources = await _mergeAsyncTask;

                DataStoreOperationOutcome dataStoreOperationOutcome;

                // Attempt 1: Retrieve from the list of merged records, the one with the same identifier from current thread.
                if (!ingestedResources.TryGetValue(identifier, out dataStoreOperationOutcome))
                {
                    // Attemp 2: Edge case scenario:
                    // Under racing conditions, it's possible that the same record (<resourceType>/<id>) is present in two bundles running at the same time.
                    // One record is updated first than the second, increasing the version of the record, and then updated once more by the second bundle.
                    // As the version is higher than the expected, the local identifier in the current thread does not match, and an alternative search for
                    // the combination <resourceType>/<id> is required.
                    var ingestedResourcesById = ingestedResources.Where(i => i.Key.ResourceType == identifier.ResourceType && i.Key.Id == identifier.Id).ToList();

                    int countOfResourcesFound = ingestedResourcesById.Count;
                    if (countOfResourcesFound == 0)
                    {
                        _logger.LogWarning(
                            "Bundle Operation {Id}. There wasn't a valid instance of '{ClassName}' for the enqueued resource. This is not an expected scenario. There are {NumberOfResource} in the operation. {PersistedResources} resources were persisted.",
                            Id,
                            nameof(DataStoreOperationOutcome),
                            _resources.Count,
                            ingestedResources?.Count);

                        throw new BundleOrchestratorException($"There wasn't a valid instance of '{nameof(DataStoreOperationOutcome)}' for the enqueued resource. This is not an expected scenario.");
                    }
                    else if (countOfResourcesFound > 1)
                    {
                        _logger.LogWarning(
                            "Bundle Operation {Id}. More than two instances of '{ClassName}' for the same resource were found. This is not an expected scenario. There are {NumberOfResource} in the operation. {PersistedResources} resources were persisted. {ResourceFound} outcomes were found for the same resource.",
                            Id,
                            nameof(DataStoreOperationOutcome),
                            _resources.Count,
                            ingestedResources?.Count,
                            countOfResourcesFound);

                        throw new BundleOrchestratorException($"More than two instances of '{nameof(DataStoreOperationOutcome)}' for the enqueued resource were found. This is not an expected scenario.");
                    }
                    else
                    {
                        dataStoreOperationOutcome = ingestedResourcesById[0].Value;
                    }
                }

                if (!dataStoreOperationOutcome.IsOperationSuccessful)
                {
                    throw dataStoreOperationOutcome.Exception;
                }

                return dataStoreOperationOutcome.UpsertOutcome;
            }
            else
            {
                // Identify duplicated resources in the same bundle.
                throw new RequestNotValidException(Core.Resources.DuplicatedResourceInABundle, OperationOutcomeConstants.IssueType.Duplicate);
            }
        }

        public async Task ReleaseResourceAsync(string reason, CancellationToken cancellationToken)
        {
            try
            {
                if (!_resources.Any())
                {
                    SetStatusSafe(BundleOrchestratorOperationStatus.WaitingForResources);
                }

                Interlocked.Decrement(ref _currentExpectedNumberOfResources);

                InitializeMergeTaskSafe(dataStore: null, cancellationToken);
            }
            catch (Exception ex)
            {
                SetStatusSafe(BundleOrchestratorOperationStatus.Failed);

                _logger.LogError(ex, "Bundle Operation {Id}. Failed while releasing a resource from a Bundle Orchestrator Operation: {ErrorMessage}", Id, ex.Message);
                throw;
            }

            await _mergeAsyncTask;
        }

        public void Cancel(string reason)
        {
            try
            {
                _logger.LogWarning("Bundle Operation {Id}. Bundle Orchestrator Operation was requested to cancel. Reason: {Reason}", Id, reason);

                SetStatusSafe(BundleOrchestratorOperationStatus.Canceled);

                Interlocked.Decrement(ref _currentExpectedNumberOfResources);
            }
            catch (Exception ex)
            {
                SetStatusSafe(BundleOrchestratorOperationStatus.Failed);

                _logger.LogError(ex, "Bundle Operation {Id}. Failed while canceling a Bundle Orchestrator Operation: {ErrorMessage}", Id, ex.Message);

                throw;
            }
        }

        private void InitializeMergeTaskSafe(IFhirDataStore dataStore, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                if (dataStore != null && _dataStore == null)
                {
                    _dataStore = dataStore;
                }

                // '_mergeAsyncTask' should be initialize only once per Back Orchestrator Operation.
                if (_mergeAsyncTask == null)
                {
                    _mergeAsyncTask = MergeAsync(cancellationToken);
                }
            }
        }

        private async Task<IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>> MergeAsync(CancellationToken cancellationToken)
        {
            try
            {
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await Task.Delay(millisecondsDelay: DelayTimeInMilliseconds, cancellationToken);
                }
                while (_resources.Count != CurrentExpectedNumberOfResources);

                if (CurrentExpectedNumberOfResources == 0)
                {
                    SetStatusSafe(BundleOrchestratorOperationStatus.Canceled);

                    return null;
                }
                else
                {
                    if (_dataStore == null)
                    {
                        throw new BundleOrchestratorException($"Bundle Operation {Id}. Data Store was not initialized. Status can't be changed to processing and resources cannot be merged.");
                    }

                    SetStatusSafe(BundleOrchestratorOperationStatus.Processing);

                    cancellationToken.ThrowIfCancellationRequested();

                    // Defines the correct execution sequence based on the HTTP Verb assigned to each resource.
                    IReadOnlyList<ResourceWrapperOperation> resources = null;
                    if (_knownHttpVerbsInOperation.Count == 1)
                    {
                        // If a single HTTP Verb is used, then there is no need to spend cycles sorting resources.
                        resources = _resources.Values.ToList();
                    }
                    else if (_knownHttpVerbsInOperation.Count > 1)
                    {
                        resources = _resources.Values.OrderBy(x => x.BundleResourceContext, _contextComparer).ToList();
                    }
                    else
                    {
                        throw new BundleOrchestratorException($"Bundle Operation {Id}. At least one HTTP Verb should be known. No HTTP Verbs were mapped so far.");
                    }

                    // Bundle Orchestrator operations will not enlist to C# transactions.
                    // The database will be responsible for handling it internally.
                    MergeOptions mergeOptions = new MergeOptions(enlistTransaction: false);
                    IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome> response = await _dataStore.MergeAsync(_resources.Values.ToList(), mergeOptions, cancellationToken);

                    SetStatusSafe(BundleOrchestratorOperationStatus.Completed);

                    return response;
                }
            }
            catch (OperationCanceledException oce)
            {
                SetStatusSafe(BundleOrchestratorOperationStatus.Canceled);

                _logger.LogError(oce, "Bundle Operation {Id}. Bundle Orchestrator Operation canceled: {ErrorMessage}", Id, oce.Message);
                throw;
            }
            catch (Exception ex)
            {
                SetStatusSafe(BundleOrchestratorOperationStatus.Failed);

                _logger.LogError(ex, "Bundle Operation {Id}. Bundle Orchestrator Operation failed: {ErrorMessage}", Id, ex.Message);
                throw;
            }
        }

        private void SetStatusSafe(BundleOrchestratorOperationStatus suggestedStatus)
        {
            lock (_lock)
            {
                if (suggestedStatus == Status)
                {
                    return;
                }
                else if (suggestedStatus == BundleOrchestratorOperationStatus.WaitingForResources && Status == BundleOrchestratorOperationStatus.Open)
                {
                    Status = BundleOrchestratorOperationStatus.WaitingForResources;
                }
                else if (suggestedStatus == BundleOrchestratorOperationStatus.Processing && Status == BundleOrchestratorOperationStatus.WaitingForResources)
                {
                    Status = BundleOrchestratorOperationStatus.Processing;
                }
                else if (suggestedStatus == BundleOrchestratorOperationStatus.Completed && Status == BundleOrchestratorOperationStatus.Processing)
                {
                    Status = BundleOrchestratorOperationStatus.Completed;
                }
                else if (suggestedStatus == BundleOrchestratorOperationStatus.Canceled || suggestedStatus == BundleOrchestratorOperationStatus.Failed)
                {
                    Status = suggestedStatus;
                }
                else
                {
                    throw new BundleOrchestratorException($"Bundle Operation {Id}. Invalid status change. Current status '{Status}'. Suggested status '{suggestedStatus}'.");
                }
            }
        }
    }
}
