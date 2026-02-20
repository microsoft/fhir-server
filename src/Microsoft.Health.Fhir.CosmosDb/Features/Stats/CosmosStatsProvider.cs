// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations.Stats;
using Microsoft.Health.Fhir.Core.Messages.Stats;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Stats
{
    /// <summary>
    /// Provides resource statistics from Cosmos DB.
    /// </summary>
    public class CosmosStatsProvider : IStatsProvider
    {
        /// <inheritdoc />
        public Task<StatsResponse> GetStatsAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException("The $stats operation is not supported for Cosmos DB data stores.");
        }
    }
}
