// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Health.SqlServer.Features.Schema;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public interface ISqlServerFhirStorageTestHelper
    {
        /// <summary>
        /// Creates and initializes a new SQL database if it does not already exist.
        /// </summary>
        /// <param name="databaseName">The name of the database to create.</param>
        /// <param name="maximumSupportedSchemaVersion">The maximum supported schema version.</param>
        /// <param name="forceIncrementalSchemaUpgrade">True if diff SQL files should be applied to upgrade the schema.</param>
        /// <param name="schemaInitializer">The schema initializer to use for database initialization. If this is not provided, a new one is created.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        Task CreateAndInitializeDatabase(string databaseName, int maximumSupportedSchemaVersion, bool forceIncrementalSchemaUpgrade, SchemaInitializer schemaInitializer = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes the specified SQL database.
        /// </summary>
        /// <param name="databaseName">The name of the database to delete.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        Task DeleteDatabase(string databaseName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes sql command.
        /// </summary>
        /// <param name="sql">SQL command text.</param>
        Task ExecuteSqlCmd(string sql);

        /// <summary>
        /// Returns a SQL connection.
        /// </summary>
        Task<SqlConnection> GetSqlConnectionAsync();
    }
}
