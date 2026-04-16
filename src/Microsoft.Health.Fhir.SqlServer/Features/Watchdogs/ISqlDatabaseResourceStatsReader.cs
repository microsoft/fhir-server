// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.SqlServer.Features.Watchdogs
{
    internal interface ISqlDatabaseResourceStatsReader
    {
        Task<SqlDatabaseResourceStats> GetLatestAsync(CancellationToken cancellationToken);
    }
}
