// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration
{
    public sealed class BundleOrchestrator : IBundleOrchestrator
    {
        /// <summary>
        /// Dictionary of current operations. At the end of an operation, it should be removed from this dictionary.
        /// Operations are indexed by their respective IDs.
        /// </summary>
        private readonly ConcurrentDictionary<Guid, IBundleOrchestratorOperation> _operationsById;

        /// <summary>
        /// Creates a new instance of <see cref="BundleOrchestrator"/>.
        /// </summary>
        /// <param name="isEnabled">Enables or disables the Bundle Orchestrator functionality.</param>
        public BundleOrchestrator(bool isEnabled)
        {
            _operationsById = new ConcurrentDictionary<Guid, IBundleOrchestratorOperation>();

            IsEnabled = isEnabled;
        }

        public bool IsEnabled { get; }

        public IBundleOrchestratorOperation CreateNewOperation(BundleOrchestratorOperationType type, string label, int expectedNumberOfResources)
        {
            EnsureArg.IsNotNullOrWhiteSpace(label, nameof(label));
            EnsureArg.IsGt(expectedNumberOfResources, 0, nameof(expectedNumberOfResources));

            BundleOrchestratorOperation newOperation = new BundleOrchestratorOperation(type, label, expectedNumberOfResources);

            if (!_operationsById.TryAdd(newOperation.Id, newOperation))
            {
                throw new BundleOrchestratorException($"An operation with ID '{newOperation.Id}' was already added to the queue.");
            }

            return newOperation;
        }

        public IBundleOrchestratorOperation GetOperation(Guid operationId)
        {
            if (!_operationsById.TryGetValue(operationId, out IBundleOrchestratorOperation operation))
            {
                throw new BundleOrchestratorException($"An operation with ID '{operationId}' was not found or unable to be completed.");
            }

            return operation;
        }

        public bool CompleteOperation(IBundleOrchestratorOperation operation)
        {
            EnsureArg.IsNotNull(operation, nameof(operation));

            if (!_operationsById.TryRemove(operation.Id, out IBundleOrchestratorOperation deletedOperation))
            {
                throw new BundleOrchestratorException($"An operation with ID '{operation.Id}' was not found or unable to be completed.");
            }

            return true;
        }
    }
}
