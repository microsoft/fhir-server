// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using EnsureThat;
using Microsoft.Health.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration
{
    public sealed class BundleOrchestrator : IBundleOrchestrator
    {
        /// <summary>
        /// Dictionary of current operations. At the end of an operation, it should be removed from this dictionary.
        /// Operations are indexed by their respective IDs.
        /// </summary>
        private readonly ConcurrentDictionary<Guid, BundleOrchestratorOperation> _operationsById;

        private readonly IScoped<IFhirDataStore> _dataStore;

        public BundleOrchestrator(bool isEnabled, IScoped<IFhirDataStore> dataStore)
        {
            EnsureArg.IsNotNull(dataStore, nameof(dataStore));

            _dataStore = dataStore;

            _operationsById = new ConcurrentDictionary<Guid, BundleOrchestratorOperation>();

            IsEnabled = isEnabled;
        }

        public bool IsEnabled { get; }

        public IBundleOrchestratorOperation CreateNewOperation(BundleOrchestratorOperationType type, string label, int expectedNumberOfResources)
        {
            EnsureArg.IsNotNullOrWhiteSpace(label, nameof(label));
            EnsureArg.IsGt(expectedNumberOfResources, 0, nameof(expectedNumberOfResources));

            var newJob = new BundleOrchestratorOperation(type, label, expectedNumberOfResources, _dataStore);

            if (!_operationsById.TryAdd(newJob.Id, newJob))
            {
                throw new BundleOrchestratorException($"A job with ID '{newJob.Id}' was already added to the queue.");
            }

            return newJob;
        }

        public bool RemoveOperation(Guid id)
        {
            if (!_operationsById.TryRemove(id, out BundleOrchestratorOperation job))
            {
                throw new BundleOrchestratorException($"A job with ID '{id}' was not found or unable to be removed from {nameof(BundleOrchestrator)}.");
            }

            return true;
        }
    }
}
