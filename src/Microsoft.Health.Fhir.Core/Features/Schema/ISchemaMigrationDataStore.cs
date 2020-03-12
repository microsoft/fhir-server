// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Schema
{
    public interface ISchemaMigrationDataStore
    {
        /// <summary>
        /// Get compatible version.
        /// </summary>
        /// <param name="maxVersion">The maximum schema version specified by code</param>
        /// /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The latest supported schema version from server.</returns>
        Task<int> GetLatestCompatibleVersionAsync(int maxVersion, CancellationToken cancellationToken);
    }
}
