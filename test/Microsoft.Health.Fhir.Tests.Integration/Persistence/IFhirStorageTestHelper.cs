// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public interface IFhirStorageTestHelper
    {
        Task DeleteAllExportJobRecordsAsync();

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
