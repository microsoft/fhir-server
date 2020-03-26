// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Messages.Get;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

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
        Task DeleteExpiredRecordsAsync();

        /// <summary>
        /// Upsert current version information.
        /// </summary>
        /// /// <param name="name">The instance name.</param>
        /// /// <param name="versions">The compatible versions.</param>
        /// /// <param name="currentVersion">The current version.</param>
        /// /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The instance name</returns>
        Task<string> UpsertInstanceSchemaInformation(string name, CompatibleVersions versions, int currentVersion, CancellationToken cancellationToken);

        /// <summary>
        /// Upsert current version information.
        /// </summary>
        /// /// <param name="name">The instance name.</param>
        /// /// <param name="schemaInformation">The schemainformation.</param>
        /// /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The instance name</returns>
        Task<string> InsertInstanceSchemaInformation(string name, SchemaInformation schemaInformation, CancellationToken cancellationToken);
    }
}
