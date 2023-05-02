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

namespace Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration
{
    public sealed class BundleOrchestratorOperation : IBundleOrchestratorOperation
    {
        private const int DelayTimeInMilliseconds = 10;

        /// <summary>
        /// List of resource to be sent to the data layer.
        /// </summary>
        private readonly ConcurrentBag<ResourceWrapperOperation> _resources;

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

        public BundleOrchestratorOperation(BundleOrchestratorOperationType type, string label, int expectedNumberOfResources)
        {
            EnsureArg.IsNotNullOrWhiteSpace(label, nameof(label));
            EnsureArg.IsGt(expectedNumberOfResources, 0, nameof(expectedNumberOfResources));

            Id = Guid.NewGuid();
            Type = type;
            Label = label;
            CreationTime = DateTime.UtcNow;
            Status = BundleOrchestratorOperationStatus.Open;

            OriginalExpectedNumberOfResources = expectedNumberOfResources;
            _currentExpectedNumberOfResources = expectedNumberOfResources;

            _resources = new ConcurrentBag<ResourceWrapperOperation>();

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

        public override string ToString()
        {
            return $"[ Job: {Id}, Label: {Label}, OriginalExpectedNumberOfResources: {OriginalExpectedNumberOfResources}, CreationTime: {CreationTime.ToString("o")}, Status: {Status} ]";
        }

        public async Task<UpsertOutcome> AppendResourceAsync(ResourceWrapperOperation resource, IFhirDataStore dataStore, CancellationToken cancellationToken)
        {
            try
            {
                if (!_resources.Any())
                {
                    SetStatusSafe(BundleOrchestratorOperationStatus.WaitingForResources);
                }

                InitializeMergeTaskSafe(dataStore, cancellationToken);

                _resources.Add(resource);
            }
            catch (Exception)
            {
                SetStatusSafe(BundleOrchestratorOperationStatus.Failed);

                // Add logging.
                throw;
            }

            var ingestedResources = await _mergeAsyncTask;
            DataStoreOperationIdentifier identifier = resource.GetIdentifier();
            DataStoreOperationOutcome dataStoreOperationOutcome = ingestedResources[identifier];
            if (!dataStoreOperationOutcome.IsOperationSuccessful)
            {
                throw dataStoreOperationOutcome.Exception;
            }

            return dataStoreOperationOutcome.UpsertOutcome;
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
            catch (Exception)
            {
                SetStatusSafe(BundleOrchestratorOperationStatus.Failed);

                // Add logging.
                throw;
            }

            await _mergeAsyncTask;
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
                        throw new BundleOrchestratorException($"Data Store was not initialized. Status can't be changed to processing and resources cannot be merged.");
                    }

                    SetStatusSafe(BundleOrchestratorOperationStatus.Processing);

                    cancellationToken.ThrowIfCancellationRequested();

                    IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome> response = await _dataStore.MergeAsync(_resources.ToList(), cancellationToken);

                    SetStatusSafe(BundleOrchestratorOperationStatus.Completed);

                    return response;
                }
            }
            catch (OperationCanceledException)
            {
                SetStatusSafe(BundleOrchestratorOperationStatus.Canceled);

                // Add logging.
                throw;
            }
            catch (Exception)
            {
                SetStatusSafe(BundleOrchestratorOperationStatus.Failed);

                // Add logging.
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
                    throw new BundleOrchestratorException($"Invalid status change. Current status '{Status}'. Suggested status '{suggestedStatus}'.");
                }
            }
        }
    }
}
