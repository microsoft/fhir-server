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
        /// Dictionary of current jobs. At the end of a job execution,
        /// the job should be removed from this dictionary.
        /// Jobs are indexed by their respective IDs.
        /// </summary>
        private readonly ConcurrentDictionary<Guid, BatchOrchestratorJob<T>> _jobsById;

        private readonly object _dataLayer;

        public BatchOrchestrator()
        {
            _jobsById = new ConcurrentDictionary<Guid, BatchOrchestratorJob<T>>();

            // Todo: Inject the data layer in use (Cosmos, SQL).
            _dataLayer = null;
        }

        public BatchOrchestratorJob<T> CreateNewJob(string label, int expectedNumberOfResources)
        {
            EnsureArg.IsNotNullOrWhiteSpace(label, nameof(label));
            EnsureArg.IsGt(expectedNumberOfResources, 0, nameof(expectedNumberOfResources));

            var newJob = new BatchOrchestratorJob<T>(label, expectedNumberOfResources, _dataLayer);

            if (!_jobsById.TryAdd(newJob.Id, newJob))
            {
                throw new BatchOrchestratorException($"A job with ID '{newJob.Id}' was already added to the queue.");
            }

            return newJob;
        }

        public bool RemoveJob(Guid id)
        {
            if (!_jobsById.TryRemove(id, out BatchOrchestratorJob<T> job))
            {
                throw new BatchOrchestratorException($"A job with ID '{id}' was not found or unable to be removed from {nameof(BatchOrchestrator<T>)}.");
            }

            return true;
        }
    }
}
