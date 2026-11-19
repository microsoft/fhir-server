// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Core.Features.FhirPackages
{
    /// <summary>
    /// Enqueues exact package installations, reusing a queued or running job for the same package.
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
        /// Returns the queued or running job for the same package, or enqueues a new one.
        /// Failed and cancelled jobs are not reused, so a later request starts a new attempt.
        /// </summary>
        /// <param name="packageId">The exact npm package name.</param>
        /// <param name="version">The exact package version.</param>
        /// <param name="source">The deployment-configured source identifier.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The active or new job.</returns>
        public async Task<JobInfo> EnqueueAsync(string packageId, string version, string source, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(packageId, nameof(packageId));
            EnsureArg.IsNotNullOrWhiteSpace(version, nameof(version));
            EnsureArg.IsNotNullOrWhiteSpace(source, nameof(source));

            IReadOnlyList<JobInfo> activeJobs = await _queueClient.GetActiveJobsByQueueTypeAsync(
                (byte)QueueType.FhirPackageInstall, returnParentOnly: false, cancellationToken);
            JobInfo activeJob = activeJobs?.FirstOrDefault(job => IsSamePackage(job, packageId, version, source));
            if (activeJob != null)
            {
                return activeJob;
            }

            var definition = new FhirPackageInstallJobDefinition
            {
                PackageId = packageId,
                Version = version,
                Source = source,
                Id = Guid.NewGuid().ToString(),
            };

            IReadOnlyList<JobInfo> jobs = await _queueClient.EnqueueAsync(
                QueueType.FhirPackageInstall, cancellationToken, definitions: definition);

            if (jobs?.Count != 1)
            {
                throw new InvalidOperationException("The package install queue returned an invalid job list.");
            }

            return jobs[0];
        }

        private static bool IsSamePackage(JobInfo job, string packageId, string version, string source)
        {
            FhirPackageInstallJobDefinition definition = job.DeserializeDefinition<FhirPackageInstallJobDefinition>();
            return definition != null &&
                string.Equals(definition.PackageId, packageId, StringComparison.Ordinal) &&
                string.Equals(definition.Version, version, StringComparison.Ordinal) &&
                string.Equals(definition.Source, source, StringComparison.Ordinal);
        }
    }
}
