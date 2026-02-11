// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Health.Fhir.Core.Messages.Stats;

namespace Microsoft.Health.Fhir.SqlServer.Features.Stats
{
    /// <summary>
    /// Handles stats requests for SQL Server.
    /// </summary>
    public class StatsHandler : IRequestHandler<StatsRequest, StatsResponse>
    {
        private readonly SqlServerStatsProvider _statsProvider;

        public StatsHandler(SqlServerStatsProvider statsProvider)
        {
            _statsProvider = statsProvider;
        }

        public async Task<StatsResponse> Handle(StatsRequest request, CancellationToken cancellationToken)
        {
            return await _statsProvider.GetStatsAsync(cancellationToken);
        }
    }
}
