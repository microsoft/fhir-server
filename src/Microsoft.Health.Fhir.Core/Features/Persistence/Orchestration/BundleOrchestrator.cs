// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration
{
    public sealed class BundleOrchestrator : IBundleOrchestrator
    {
        private static readonly TimeSpan _longRunningOperationTimeThreshold = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan _longRunningOperationsStatusCheckInterval = TimeSpan.FromMinutes(3);
        private static readonly BundleOrchestratorOperationStatus[] _operationsCompletedStatus = new BundleOrchestratorOperationStatus[]
        {
            BundleOrchestratorOperationStatus.Completed,
            BundleOrchestratorOperationStatus.Failed,
            BundleOrchestratorOperationStatus.Canceled,
        };

        /// <summary>
        /// Dictionary of current operations. At the end of an operation, it should be removed from this dictionary.
        /// Operations are indexed by their respective IDs.
        /// </summary>
        private readonly ConcurrentDictionary<Guid, IBundleOrchestratorOperation> _operationsById;

        private readonly ILogger<BundleOrchestrator> _logger;

        private readonly int _maxExecutionTimeInSeconds;

        private readonly object _lockObject;

        private DateTimeOffset _latestLongRunningOperationStatusCheck;

        /// <summary>
        /// Creates a new instance of <see cref="BundleOrchestrator"/>.
        /// </summary>
        /// <param name="bundleConfiguration">Bundle configuration.</param>
        /// <param name="logger">Logging component.</param>
        public BundleOrchestrator(IOptions<BundleConfiguration> bundleConfiguration, ILogger<BundleOrchestrator> logger)
        {
            EnsureArg.IsNotNull(bundleConfiguration, nameof(bundleConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            IsEnabled = bundleConfiguration.Value.SupportsBundleOrchestrator;
            _maxExecutionTimeInSeconds = bundleConfiguration.Value.MaxExecutionTimeInSeconds;

            _logger = logger;
            _operationsById = new ConcurrentDictionary<Guid, IBundleOrchestratorOperation>();

            _latestLongRunningOperationStatusCheck = DateTimeOffset.UtcNow.Add(_longRunningOperationsStatusCheckInterval);

            _lockObject = new object();
        }

        public bool IsEnabled { get; }

        public IBundleOrchestratorOperation CreateNewOperation(BundleOrchestratorOperationType type, string label, int expectedNumberOfResources)
        {
            EnsureArg.IsNotNullOrWhiteSpace(label, nameof(label));
            EnsureArg.IsGt(expectedNumberOfResources, 0, nameof(expectedNumberOfResources));

            BundleOrchestratorOperation newOperation = new BundleOrchestratorOperation(type, label, expectedNumberOfResources, _logger, maxExecutionTimeInSeconds: _maxExecutionTimeInSeconds);

            CheckLongRunningOperationStatus();

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

            if (_operationsById.TryRemove(operation.Id, out IBundleOrchestratorOperation deletedOperation))
            {
                deletedOperation.Clear();
            }
            else
            {
                throw new BundleOrchestratorException($"An operation with ID '{operation.Id}' was not found or unable to be completed.");
            }

            return true;
        }

        private void CheckLongRunningOperationStatus()
        {
            // Ensuring most of the checks don't need to acquire the lock.
            if (DateTimeOffset.UtcNow - _latestLongRunningOperationStatusCheck < _longRunningOperationsStatusCheckInterval)
            {
                return;
            }

            lock (_lockObject)
            {
                // Minimizing the access to the lock block.
                if (DateTimeOffset.UtcNow - _latestLongRunningOperationStatusCheck < _longRunningOperationsStatusCheckInterval)
                {
                    return;
                }

                _latestLongRunningOperationStatusCheck = DateTimeOffset.UtcNow;
            }

            if (!_operationsById.Any())
            {
                return;
            }

            var longRunningOperations = _operationsById
                .Where(o => o.Value.ElapsedTime > _longRunningOperationTimeThreshold)
                .Select(o => o.Value)
                .ToList();

            if (longRunningOperations.Any())
            {
                _logger.LogWarning("BundleOrchestrator: There are {LongRunningOperationCount} long running operations in the orchestrator queue.", longRunningOperations.Count);

                StringBuilder text = new StringBuilder();
                foreach (IBundleOrchestratorOperation longRunningOperation in longRunningOperations)
                {
                    if (_operationsCompletedStatus.Contains(longRunningOperation.Status))
                    {
                        if (_operationsById.TryRemove(longRunningOperation.Id, out _))
                        {
                            text.AppendLine($"{longRunningOperation.Id} - {longRunningOperation.Status} - {longRunningOperation.ElapsedTime} - Removed. ");
                            longRunningOperation.Clear();
                        }
                        else
                        {
                            text.AppendLine($"{longRunningOperation.Id} - {longRunningOperation.Status} - {longRunningOperation.ElapsedTime} - Failed to remove. ");
                        }
                    }
                    else
                    {
                        text.AppendLine($"{longRunningOperation.Id} - {longRunningOperation.Status} - {longRunningOperation.ElapsedTime}. ");
                    }

                    _logger.LogInformation("BundleOrchestrator: Long running operation details: {OperationDetails}", text.ToString());
                }
            }
        }
    }
}
