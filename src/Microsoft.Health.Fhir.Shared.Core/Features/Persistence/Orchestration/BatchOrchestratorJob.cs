// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration
{
    public sealed class BatchOrchestratorJob<T>
        where T : class
    {
        private readonly ConcurrentBag<T> _resources;

        private readonly object _dataLayer;

        private readonly object _lock;

        private int _currentExpectedNumberOfResources;

        public BatchOrchestratorJob(string label, int expectedNumberOfResources, object dataLayer)
        {
            EnsureArg.IsNotNullOrWhiteSpace(label, nameof(label));
            EnsureArg.IsGt(expectedNumberOfResources, 0, nameof(expectedNumberOfResources));

            Id = Guid.NewGuid();
            Label = label;
            CreationTime = DateTime.UtcNow;
            Status = BatchOrchestratorJobStatus.Open;

            OriginalExpectedNumberOfResources = expectedNumberOfResources;
            _currentExpectedNumberOfResources = expectedNumberOfResources;

            _resources = new ConcurrentBag<T>();
            _dataLayer = dataLayer;

            _lock = new object();
        }

        public Guid Id { get; private set; }

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

        public BatchOrchestratorJobStatus Status { get; private set; }

        public override string ToString()
        {
            return $"[ Job: {Id}, Label: {Label}, OriginalExpectedNumberOfResources: {OriginalExpectedNumberOfResources}, CreationTime: {CreationTime.ToString("o")}, Status: {Status} ]";
        }

        public void AppendResource(T resource)
        {
            if (!_resources.Any())
            {
                SetStatus(BatchOrchestratorJobStatus.Waiting);
            }

            // Add validation to avoid null references to job.
            _resources.Add(resource);

            if (_resources.Count == CurrentExpectedNumberOfResources)
            {
                SetStatus(BatchOrchestratorJobStatus.Processing);
            }
        }

        public void ReleaseResource(string reason)
        {
            if (!_resources.Any())
            {
                SetStatus(BatchOrchestratorJobStatus.Waiting);
            }

            Interlocked.Decrement(ref _currentExpectedNumberOfResources);

            lock (_lock)
            {
                if (_currentExpectedNumberOfResources == 0)
                {
                    SetStatus(BatchOrchestratorJobStatus.Canceled);
                }
            }

            if (_resources.Count == CurrentExpectedNumberOfResources)
            {
                SetStatus(BatchOrchestratorJobStatus.Processing);
            }
        }

        private void SetStatus(BatchOrchestratorJobStatus suggestedStatus)
        {
            lock (_lock)
            {
                if (suggestedStatus == BatchOrchestratorJobStatus.Waiting && Status == BatchOrchestratorJobStatus.Open)
                {
                    Status = BatchOrchestratorJobStatus.Waiting;
                }
                else if (suggestedStatus == BatchOrchestratorJobStatus.Processing && Status == BatchOrchestratorJobStatus.Waiting)
                {
                    Status = BatchOrchestratorJobStatus.Processing;
                }
                else if (suggestedStatus == BatchOrchestratorJobStatus.Canceled)
                {
                    Status = BatchOrchestratorJobStatus.Canceled;
                }
            }
        }
    }
}
