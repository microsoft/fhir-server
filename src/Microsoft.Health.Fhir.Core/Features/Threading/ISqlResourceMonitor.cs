// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Threading
{
    /// <summary>
    /// Extended resource monitor interface that includes SQL Server-specific performance metrics.
    /// </summary>
    public interface ISqlResourceMonitor : IRuntimeResourceMonitor
    {
        /// <summary>
        /// Gets the current SQL connection pool utilization percentage.
        /// </summary>
        /// <returns>The connection pool utilization as a percentage (0-100).</returns>
        Task<double> GetConnectionPoolUtilizationAsync();

        /// <summary>
        /// Gets the current SQL wait statistics for performance monitoring.
        /// </summary>
        /// <returns>A collection of top SQL wait types and their metrics.</returns>
        Task<IEnumerable<SqlWaitStatistic>> GetTopSqlWaitStatisticsAsync();

        /// <summary>
        /// Detects if there are any blocked SQL processes that might indicate resource contention.
        /// </summary>
        /// <returns>True if blocked processes are detected; otherwise, false.</returns>
        Task<bool> HasBlockedProcessesAsync();

        /// <summary>
        /// Gets the current SQL transaction log usage percentage.
        /// </summary>
        /// <returns>The transaction log usage as a percentage (0-100).</returns>
        Task<double> GetTransactionLogUsageAsync();

        /// <summary>
        /// Determines if the SQL Server is under pressure based on multiple metrics.
        /// </summary>
        /// <returns>True if SQL Server is under pressure; otherwise, false.</returns>
        Task<bool> IsSqlUnderPressureAsync();

        /// <summary>
        /// Gets the current number of active SQL connections.
        /// </summary>
        /// <returns>The number of active connections.</returns>
        Task<int> GetActiveConnectionCountAsync();

        /// <summary>
        /// Gets metrics about deadlocks occurring in the database.
        /// </summary>
        /// <returns>Deadlock statistics for monitoring.</returns>
        Task<SqlDeadlockStatistics> GetDeadlockStatisticsAsync();
    }
}
