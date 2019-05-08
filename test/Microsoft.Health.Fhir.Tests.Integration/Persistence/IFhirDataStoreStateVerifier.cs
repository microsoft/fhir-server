// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Provides methods for verifying hard deletion.
    /// Call <see cref="GetSnapshotToken"/> at the start of a test,
    /// add and update resources, hard delete them, and then verify that the
    /// all the new resources were fully deleted at the end by calling <see cref="ValidateSnapshotTokenIsCurrent"/>.
    /// </summary>
    public interface IFhirDataStoreStateVerifier
    {
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
