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
        protected override async ValueTask<DatabaseParameters> GetRemoteDataAsync(string partitionId, bool async, CancellationToken cancellationToken)
        {
            try
            {
                IReadOnlyList<(string Id, double Number)> rows = await QueryAsync(
                    partitionId,
                    names =>
                    {
                        string table = names.GetObjectName("dbo.Parameters");
#pragma warning disable CA2100 // The only interpolated value is an object name quoted by SqlObjectNameFormatter.
                        var command = new SqlCommand($"IF object_id(@Table) IS NOT NULL SELECT Id, Number FROM {table} WHERE Number IS NOT NULL");
#pragma warning restore CA2100
                        command.Parameters.AddWithValue("@Table", table);
                        return command;
                    },
                    reader => (reader.GetString(0), Convert.ToDouble(reader.GetValue(1))),
                    async,
                    cancellationToken);

                var numbers = new Dictionary<string, double>();
                foreach ((string id, double number) in rows)
                {
                    numbers.TryAdd(id, number);
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
