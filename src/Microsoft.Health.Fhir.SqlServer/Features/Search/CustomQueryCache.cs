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

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    /// <summary>
    /// Caches each partition's hand-tuned search procedures (<c>CustomQuery_&lt;hash&gt;</c>), reloaded every minute.
    /// </summary>
    internal sealed class CustomQueryCache : SqlPartitionCache<CustomQueryProcedures>
    {
        internal static readonly TimeSpan Expiry = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Initializes a new instance of the <see cref="CustomQueryCache"/> class.
        /// </summary>
        /// <param name="connectionBuilder">Builds SQL connections.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="timeProvider">The time provider; defaults to <see cref="TimeProvider.System"/>.</param>
        public CustomQueryCache(ISqlConnectionBuilder connectionBuilder, ILogger<CustomQueryCache> logger, TimeProvider timeProvider = null)
            : base(connectionBuilder, logger, Expiry, timeProvider)
        {
        }

        /// <inheritdoc />
        [SuppressMessage("Performance", "CA1849:Call async methods when in an async method", Justification = "The async: false branch must run synchronously.")]
        protected override async ValueTask<CustomQueryProcedures> GetRemoteDataAsync(IPartitionInfo partition, bool async, CancellationToken cancellationToken)
        {
            using SqlConnection connection = await OpenConnectionAsync(partition, async, cancellationToken);
            using var command = new SqlCommand("SELECT name FROM sys.objects WHERE type = 'p' AND name LIKE 'CustomQuery[_]%'", connection);
            using SqlDataReader reader = async ? await command.ExecuteReaderAsync(cancellationToken) : command.ExecuteReader();

            var procedureNames = new List<string>();
            while (async ? await reader.ReadAsync(cancellationToken) : reader.Read())
            {
                procedureNames.Add(reader.GetString(0));
            }

            return CustomQueryProcedures.Create(procedureNames);
        }
    }
}
