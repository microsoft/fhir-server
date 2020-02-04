// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public interface ISqlServerFhirStorageTestHelper
    {
        /// <summary>
        /// Creates and initializes a new SQL database.
        /// </summary>
        /// <param name="databaseName">The name of the database to create.</param>
        /// <param name="applySqlSchemaSnapshot">True if the latest snapshot schema file should be run, false if diff SQL files should be applied to upgrade the schema.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        Task CreateAndInitializeDatabase(string databaseName, bool applySqlSchemaSnapshot, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes the specified SQL database.
        /// </summary>
        /// <param name="databaseName">The name of the database to delete.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        Task DeleteDatabase(string databaseName, CancellationToken cancellationToken = default);
    }
}
