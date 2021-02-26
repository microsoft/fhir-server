// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public interface IFhirStorageTestHelper
    {
        /// <summary>
        /// Deletes all export job records from the database.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task.</returns>
        Task DeleteAllExportJobRecordsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes the specified export job record from the database.
        /// </summary>
        /// <param name="id">The id of the job to delete.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task.</returns>
        Task DeleteExportJobRecordAsync(string id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes specified search parameter statuses from the database.
        /// </summary>
        /// <param name="uri">The string URI of the status to be deleted.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task.</returns>
        Task DeleteSearchParameterStatusAsync(string uri, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes all reindex job records from the database.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task.</returns>
        Task DeleteAllReindexJobRecordsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes the specified reindex job record from the database.
        /// </summary>
        /// <param name="id">The id of the job to delete.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task.</returns>
        Task DeleteReindexJobRecordAsync(string id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a token representing the state of the database.
        /// </summary>
        /// <returns>The state token</returns>
        Task<object> GetSnapshotToken();

        /// <summary>
        /// Verifies that the given state token still represents the state of the database
        /// </summary>
        /// <param name="snapshotToken">The state token returned by <see cref="GetSnapshotToken"/></param>
        /// <returns>A task.</returns>
        Task ValidateSnapshotTokenIsCurrent(object snapshotToken);
    }
}
