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
        /// <returns>The latest supported schema versions from server.</returns>
        Task<GetCompatibilityVersionResponse> GetLatestCompatibleVersionAsync(CancellationToken cancellationToken);
    }
}
