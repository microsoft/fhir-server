// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Caching;
using Microsoft.Health.Fhir.Core.Features.Partitions;
using Microsoft.Health.Fhir.SqlServer.Features.Partitions;
using Microsoft.Health.SqlServer;

namespace Microsoft.Health.Fhir.SqlServer.Features.Caching
{
    /// <summary>
    /// A <see cref="PartitionCache{T}"/> loaded from each partition's SQL database.
    /// </summary>
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
        /// Opens a connection whose current database is the partition's database, so loaders use ordinary
        /// one- and two-part names and can't read another partition's data by mistake.
        /// </summary>
        /// <param name="partitionId">The partition ID, or <see langword="null"/> for the database in the connection string.</param>
        /// <param name="async">Whether to use asynchronous calls.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>An open connection. The caller disposes it.</returns>
        [SuppressMessage("Performance", "CA1849:Call async methods when in an async method", Justification = "The async: false branch must run synchronously.")]
        protected async ValueTask<SqlConnection> OpenConnectionAsync(string partitionId, bool async, CancellationToken cancellationToken)
        {
            string database = GetDatabase(partitionId);
            SqlConnection connection = async
                ? await _connectionBuilder.GetSqlConnectionAsync(database, cancellationToken: cancellationToken)
                : _connectionBuilder.GetSqlConnection(database);

            try
            {
                if (async)
                {
                    await connection.OpenAsync(cancellationToken);
                }
                else
                {
                    connection.Open();
                }

                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
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
