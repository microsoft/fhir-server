// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public static class SqlCommandExtensions
    {
        public static async Task ExecuteNonQueryAsync<TLogger>(this SqlCommand cmd, ISqlRetryService retryService, ILogger<TLogger> logger, CancellationToken cancellationToken, string logMessage = null)
        {
            await retryService.ExecuteSql(cmd, async (sql, cancel) => await sql.ExecuteNonQueryAsync(cancel), logger, logMessage, cancellationToken);
        }

        public static async Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult, TLogger>(this SqlCommand cmd, ISqlRetryService retryService, Func<SqlDataReader, TResult> readerToResult, ILogger<TLogger> logger, CancellationToken cancellationToken, string logMessage = null)
        {
            return await retryService.ExecuteReaderAsync(cmd, readerToResult, logger, logMessage, cancellationToken);
        }

        public static async Task<object> ExecuteScalarAsync<TLogger>(this SqlCommand cmd, ISqlRetryService retryService, ILogger<TLogger> logger, CancellationToken cancellationToken, string logMessage = null)
        {
            object scalar = null;
            await retryService.ExecuteSql(cmd, async (sql, cancel) => { scalar = await sql.ExecuteScalarAsync(cancel); }, logger, logMessage, cancellationToken);
            return scalar;
        }
    }
}
