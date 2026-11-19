// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Core.Features.FhirPackages
{
    /// <summary>
    /// Enqueues exact package installations with deterministic queue definitions.
    /// </summary>
    public sealed class FhirPackageInstallJobEnqueuer
    {
        private readonly IQueueClient _queueClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="FhirPackageInstallJobEnqueuer"/> class.
        /// </summary>
        /// <param name="queueClient">The durable queue client.</param>
        public FhirPackageInstallJobEnqueuer(IQueueClient queueClient)
        {
            _queueClient = EnsureArg.IsNotNull(queueClient, nameof(queueClient));
        }

        /// <summary>
        /// Returns the existing job for the definition, or enqueues a new one.
        /// A retry of a failed job must set <see cref="FhirPackageInstallJobDefinition.RetryOfJobId"/>.
        /// </summary>
        /// <param name="definition">The exact package request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The existing or new job.</returns>
        public async Task<JobInfo> EnqueueAsync(FhirPackageInstallJobDefinition definition, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(definition, nameof(definition));
            EnsureArg.IsNotNullOrWhiteSpace(definition.PackageId, nameof(definition.PackageId));
            EnsureArg.IsNotNullOrWhiteSpace(definition.Version, nameof(definition.Version));
            EnsureArg.IsNotNullOrWhiteSpace(definition.Source, nameof(definition.Source));
            if (definition.RetryOfJobId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(definition), "A retry job id must be positive.");
            }

            // Concurrent SQL enqueues can race between the existing-job lookup and insert. A second lookup
            // returns the winner rather than reporting a successful enqueue without a job.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                IReadOnlyList<JobInfo> jobs = await _queueClient.EnqueueAsync(
                    QueueType.FhirPackageInstall, cancellationToken, definitions: definition);

                if (jobs?.Count == 1)
                {
                    return jobs[0];
                }

                if (jobs == null || jobs.Count > 1)
                {
                    throw new InvalidOperationException("The package install queue returned an invalid job list.");
                }
            }

            throw new InvalidOperationException("The package install queue did not return the enqueued job.");
        }
    }
}
