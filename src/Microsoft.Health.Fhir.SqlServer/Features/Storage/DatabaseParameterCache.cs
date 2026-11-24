// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Partitions;
using Microsoft.Health.Fhir.SqlServer.Features.Caching;
using Microsoft.Health.SqlServer;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// Caches each partition's <c>dbo.Parameters</c> numbers, reloaded every 10 minutes.
    /// </summary>
    internal sealed class DatabaseParameterCache : SqlPartitionCache<DatabaseParameters>
    {
        internal static readonly TimeSpan Expiry = TimeSpan.FromMinutes(10);

        private readonly ILogger<DatabaseParameterCache> _logger;

        public DatabaseParameterCache(ISqlConnectionBuilder connectionBuilder, IPartitionManager partitionManager, ILogger<DatabaseParameterCache> logger, TimeProvider timeProvider = null)
            : base(connectionBuilder, partitionManager, logger, Expiry, timeProvider)
        {
            _logger = logger;
        }

        // A failed read uses the defaults until the next reload, as before: these are optional settings, so they must not fail requests.
        [SuppressMessage("Performance", "CA1849:Call async methods when in an async method", Justification = "The async: false branch must run synchronously.")]
        protected override async ValueTask<DatabaseParameters> GetRemoteDataAsync(string partitionId, bool async, CancellationToken cancellationToken)
        {
            try
            {
                using SqlConnection connection = await OpenConnectionAsync(partitionId, async, cancellationToken);
                using var command = new SqlCommand("IF object_id('dbo.Parameters') IS NOT NULL SELECT Id, Number FROM dbo.Parameters WHERE Number IS NOT NULL", connection);
                using SqlDataReader reader = async ? await command.ExecuteReaderAsync(cancellationToken) : command.ExecuteReader();

                var numbers = new Dictionary<string, double>();
                while (async ? await reader.ReadAsync(cancellationToken) : reader.Read())
                {
                    numbers.TryAdd(reader.GetString(0), Convert.ToDouble(reader.GetValue(1)));
                }

                return new DatabaseParameters(numbers);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Failed to read dbo.Parameters for partition '{PartitionId}'. Using defaults until the next reload.", partitionId);
                return DatabaseParameters.Empty;
            }
        }
    }
}
