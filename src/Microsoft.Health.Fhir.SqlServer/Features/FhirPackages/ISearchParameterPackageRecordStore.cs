// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.SqlServer.Features.FhirPackages
{
    /// <summary>
    /// Reads and writes applied-package records.
    /// </summary>
    /// <remarks>
    /// The SQL implementation depends on the applied-package tables (ADO 206132), which don't exist yet. Those tables key
    /// the owned canonical URLs uniquely, so one URL has exactly one owning package.
    /// </remarks>
    public interface ISearchParameterPackageRecordStore
    {
        /// <summary>
        /// Gets the committed record for a package.
        /// </summary>
        /// <param name="packageId">The package id.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The record, or <c>null</c> if the package isn't installed.</returns>
        Task<SearchParameterPackageRecord> GetAsync(string packageId, CancellationToken cancellationToken);

        /// <summary>
        /// Adds a record inside the ambient SQL transaction that writes the package's SearchParameters, so both commit together.
        /// </summary>
        /// <remarks>
        /// Must fail, rolling back the transaction, if a record for the package already exists or another package already
        /// owns one of the canonical URLs.
        /// </remarks>
        /// <param name="record">The record to add.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the record is written in the ambient transaction.</returns>
        Task AddAsync(SearchParameterPackageRecord record, CancellationToken cancellationToken);
    }
}
