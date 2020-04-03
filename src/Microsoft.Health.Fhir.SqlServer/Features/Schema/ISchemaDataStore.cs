// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Messages.Get;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema
{
    public interface ISchemaDataStore
    {
        /// <summary>
        /// Get compatible version.
        /// </summary>
        /// /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The latest supported schema version from server.</returns>
        Task<GetCompatibilityVersionResponse> GetLatestCompatibleVersionAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Get current version information.
        /// </summary>
        /// /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The current schema versions information</returns>
        Task<GetCurrentVersionResponse> GetCurrentVersionAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Delete expired instance schema information.
        /// </summary>
        /// <returns>A task</returns>
        Task DeleteExpiredRecords();

        /// <summary>
        /// Upsert current version information.
        /// </summary>
        /// /// <param name="name">The instance name.</param>
        /// /// <param name="schemaInformation">The Schema information</param>
        /// /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A Task</returns>
        Task UpsertInstanceSchemaInformation(string name, SchemaInformation schemaInformation, CancellationToken cancellationToken);

        /// <summary>
        /// Upsert current version information.
        /// </summary>
        /// /// <param name="name">The instance name.</param>
        /// /// <param name="schemaInformation">The SchemaInformation.</param>
        /// /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A Task</returns>
        Task InsertInstanceSchemaInformation(string name, SchemaInformation schemaInformation, CancellationToken cancellationToken);

        /// <summary>
        /// Get current schema version.
        /// </summary>
        /// <returns>The current version</returns>
        int? GetCurrentSchemaVersion();
    }
}
