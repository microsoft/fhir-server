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
        /// <param name="partitionManager">Resolves a partition ID to its database.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="timeProvider">The time provider; defaults to <see cref="TimeProvider.System"/>.</param>
        public CustomQueryCache(ISqlConnectionBuilder connectionBuilder, IPartitionManager partitionManager, ILogger<CustomQueryCache> logger, TimeProvider timeProvider = null)
            : base(connectionBuilder, partitionManager, logger, Expiry, timeProvider)
        {
        }

        /// <inheritdoc />
        protected override async ValueTask<CustomQueryProcedures> GetRemoteDataAsync(string partitionId, bool async, CancellationToken cancellationToken)
        {
            IReadOnlyList<string> procedureNames = await QueryAsync(
                partitionId,
#pragma warning disable CA2100 // The only interpolated value is an object name quoted by SqlObjectNameFormatter.
                names => new SqlCommand($"SELECT name FROM {names.GetObjectName("sys.objects")} WHERE type = 'p' AND name LIKE 'CustomQuery[_]%'"),
#pragma warning restore CA2100
                reader => reader.GetString(0),
                async,
                cancellationToken);

            return CustomQueryProcedures.Create(procedureNames);
        }
    }
}
