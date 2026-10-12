// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.FhirPackages
{
    /// <summary>
    /// A validated set of package resources of one type, ready to be written.
    /// </summary>
    public interface IFhirPackageInstallPlan
    {
        /// <summary>
        /// Gets the resource type the plan installs.
        /// </summary>
        string ResourceType { get; }

        /// <summary>
        /// Gets the canonical URLs of the resources the plan installs.
        /// </summary>
        IReadOnlyList<string> CanonicalUrls { get; }

        /// <summary>
        /// Writes the validated resources.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the resources are written.</returns>
        Task ApplyAsync(CancellationToken cancellationToken);
    }
}
