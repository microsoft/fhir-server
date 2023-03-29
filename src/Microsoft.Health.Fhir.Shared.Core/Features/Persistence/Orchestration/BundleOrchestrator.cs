// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration
{
    public sealed class BundleOrchestrator<T> : IBundleOrchestrator<T>
        where T : class
    {
        /// <summary>
        /// Dictionary of current operations. At the end of an operation, it should be removed from this dictionary.
        /// Operations are indexed by their respective IDs.
        /// </summary>
        private readonly ConcurrentDictionary<Guid, BundleOrchestratorOperation<T>> _operationsById;

        private readonly object _dataLayer;

        public BundleOrchestrator(object datalayer)
        {
            EnsureArg.IsNotNull(datalayer, nameof(datalayer));

            // Todo: Inject the data layer in use (Cosmos, SQL).
            _dataLayer = datalayer;

            _operationsById = new ConcurrentDictionary<Guid, BundleOrchestratorOperation<T>>();
        }

        public IBundleOrchestratorOperation<T> CreateNewOperation(BundleOrchestratorOperationType type, string label, int expectedNumberOfResources)
        {
            EnsureArg.IsNotNullOrWhiteSpace(label, nameof(label));
            EnsureArg.IsGt(expectedNumberOfResources, 0, nameof(expectedNumberOfResources));

            var newJob = new BundleOrchestratorOperation<T>(type, label, expectedNumberOfResources, _dataLayer);

            if (!_operationsById.TryAdd(newJob.Id, newJob))
            {
                throw new BundleOrchestratorException($"A job with ID '{newJob.Id}' was already added to the queue.");
            }

            return newJob;
        }

        public bool RemoveOperation(Guid id)
        {
            if (!_operationsById.TryRemove(id, out BundleOrchestratorOperation<T> job))
            {
                throw new BundleOrchestratorException($"A job with ID '{id}' was not found or unable to be removed from {nameof(BundleOrchestrator<T>)}.");
            }

            return true;
        }
    }
}
