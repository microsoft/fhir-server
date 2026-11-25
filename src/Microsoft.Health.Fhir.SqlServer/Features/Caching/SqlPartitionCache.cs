// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Caching;
using Microsoft.Health.Fhir.Core.Features.Partitions;
using Microsoft.Health.Fhir.SqlServer.Features.Partitions;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer;

namespace Microsoft.Health.Fhir.SqlServer.Features.Caching
{
    /// <summary>
    /// A <see cref="PartitionCache{T}"/> loaded from each partition's SQL database.
    /// </summary>
    /// <remarks>
    /// Queries run on the shared connection string, like the rest of partition routing, so every partition uses the same
    /// connection pool. Object names are qualified with the partition's database through <see cref="SqlObjectNameFormatter"/>.
    /// </remarks>
    /// <typeparam name="T">The cached value type.</typeparam>
    internal abstract class SqlPartitionCache<T> : PartitionCache<T>
    {
        private readonly ISqlConnectionBuilder _connectionBuilder;
        private readonly IPartitionManager _partitionManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="SqlPartitionCache{T}"/> class.
        /// </summary>
        /// <param name="connectionBuilder">Builds SQL connections.</param>
        /// <param name="partitionManager">Resolves a partition ID to its database.</param>
        /// <param name="logger">The logger used to report failed refreshes.</param>
        /// <param name="expiry">How long a loaded value is used before it is reloaded, or <see langword="null"/> to keep it.</param>
        /// <param name="timeProvider">The time provider; defaults to <see cref="TimeProvider.System"/>.</param>
        protected SqlPartitionCache(ISqlConnectionBuilder connectionBuilder, IPartitionManager partitionManager, ILogger logger, TimeSpan? expiry = null, TimeProvider timeProvider = null)
            : base(logger, expiry, timeProvider)
        {
            _connectionBuilder = EnsureArg.IsNotNull(connectionBuilder, nameof(connectionBuilder));
            _partitionManager = EnsureArg.IsNotNull(partitionManager, nameof(partitionManager));
        }

        /// <summary>
        /// Runs a query against a partition's database and reads every row.
        /// </summary>
        /// <typeparam name="TRow">The row type.</typeparam>
        /// <param name="partitionId">The partition ID, or <see langword="null"/> for the database in the connection string.</param>
        /// <param name="createCommand">Builds the command, taking object names from the formatter so they target the partition's database.</param>
        /// <param name="readRow">Reads one row.</param>
        /// <param name="async">Whether to use asynchronous calls.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The rows.</returns>
        [SuppressMessage("Performance", "CA1849:Call async methods when in an async method", Justification = "The async: false branch must run synchronously.")]
        protected async ValueTask<IReadOnlyList<TRow>> QueryAsync<TRow>(
            string partitionId,
            Func<SqlObjectNameFormatter, SqlCommand> createCommand,
            Func<SqlDataReader, TRow> readRow,
            bool async,
            CancellationToken cancellationToken)
        {
            using SqlCommand command = createCommand(new SqlObjectNameFormatter(GetDatabase(partitionId)));
            using SqlConnection connection = async
                ? await _connectionBuilder.GetSqlConnectionAsync(cancellationToken: cancellationToken)
                : _connectionBuilder.GetSqlConnection();

            if (async)
            {
                await connection.OpenAsync(cancellationToken);
            }
            else
            {
                connection.Open();
            }

            command.Connection = connection;
            using SqlDataReader reader = async ? await command.ExecuteReaderAsync(cancellationToken) : command.ExecuteReader();

            var rows = new List<TRow>();
            while (async ? await reader.ReadAsync(cancellationToken) : reader.Read())
            {
                rows.Add(readRow(reader));
            }

            return rows;
        }

        private string GetDatabase(string partitionId)
        {
            if (partitionId == null)
            {
                return null;
            }

            if (!_partitionManager.TryGetPartition(partitionId, out IPartitionInfo partition))
            {
                throw new ArgumentException($"Partition '{partitionId}' is not configured.", nameof(partitionId));
            }

            return partition.GetDatabase();
        }
    }
}
