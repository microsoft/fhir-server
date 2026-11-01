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
    /// Validates and installs the package resources of one resource type.
    /// </summary>
    /// <remarks>
    /// Installation is two-phase: every handler validates its complete set before any handler writes,
    /// so one invalid resource fails the whole package.
    /// </remarks>
    public interface IFhirPackageResourceHandler
    {
        /// <summary>
        /// Gets the resource type this handler installs.
        /// </summary>
        string ResourceType { get; }

        /// <summary>
        /// Validates every entry of <see cref="ResourceType"/> in the package as one unit, without writing anything.
        /// </summary>
        /// <param name="entries">All package-root entries of <see cref="ResourceType"/>.</param>
        /// <param name="package">The package being installed.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A plan that writes the validated resources when applied.</returns>
        /// <exception cref="FhirPackageException">One or more entries are invalid; the exception carries every issue found.</exception>
        Task<IFhirPackageInstallPlan> ValidateAsync(IReadOnlyList<FhirPackageEntry> entries, FhirPackage package, CancellationToken cancellationToken);
    }
}
