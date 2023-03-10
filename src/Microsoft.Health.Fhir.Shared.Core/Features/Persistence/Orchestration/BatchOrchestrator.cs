// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration
{
    public sealed class BatchOrchestrator<T> : IBatchOrchestrator<T>
        where T : class
    {
        /// <summary>
        /// Dictionary of current operations. At the end of an operation, it should be removed from this dictionary.
        /// Operations are indexed by their respective IDs.
        /// </summary>
        private readonly ConcurrentDictionary<Guid, BatchOrchestratorOperation<T>> _operationsById;

        private readonly object _dataLayer;

        public BatchOrchestrator(object datalayer)
        {
            EnsureArg.IsNotNull(datalayer, nameof(datalayer));

            // Todo: Inject the data layer in use (Cosmos, SQL).
            _dataLayer = datalayer;

            _operationsById = new ConcurrentDictionary<Guid, BatchOrchestratorOperation<T>>();
        }

        public IBatchOrchestratorOperation<T> CreateNewOperation(BatchOrchestratorOperationType type, string label, int expectedNumberOfResources)
        {
            EnsureArg.IsNotNullOrWhiteSpace(label, nameof(label));
            EnsureArg.IsGt(expectedNumberOfResources, 0, nameof(expectedNumberOfResources));

            var newJob = new BatchOrchestratorOperation<T>(type, label, expectedNumberOfResources, _dataLayer);

            if (!_operationsById.TryAdd(newJob.Id, newJob))
            {
                throw new BatchOrchestratorException($"A job with ID '{newJob.Id}' was already added to the queue.");
            }

            return newJob;
        }

        public bool RemoveOperation(Guid id)
        {
            if (!_operationsById.TryRemove(id, out BatchOrchestratorOperation<T> job))
            {
                throw new BatchOrchestratorException($"A job with ID '{id}' was not found or unable to be removed from {nameof(BatchOrchestrator<T>)}.");
            }

            return true;
        }
    }
}
