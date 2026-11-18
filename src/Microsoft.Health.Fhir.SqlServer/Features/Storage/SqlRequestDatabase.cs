// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.SqlServer.Features.Partitions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal sealed class SqlRequestDatabase(
        RequestContextAccessor<IFhirRequestContext> requestContextAccessor,
        PartitionManager partitionManager,
        ILogger<SqlRequestDatabase> logger)
    {
        private SqlObjectNameFormatter _target;
        private object _targetLock;

        internal SqlObjectNameFormatter GetTarget()
        {
            var target = Volatile.Read(ref _target);
            if (target != null)
            {
                return target;
            }

            // Service-wide SQL can run before authentication assigns a partition. Do not pin it.
            string partitionId = requestContextAccessor.RequestContext?.PartitionId;
            if (partitionId == null || partitionManager.Partitions.Count == 0)
            {
                return SqlObjectNameFormatter.ApplicationDatabase;
            }

            return LazyInitializer.EnsureInitialized(ref _target, ref _targetLock, () =>
            {
                if (!partitionManager.TryGetDatabaseName(partitionId, out string databaseName))
                {
                    logger.LogError("Cannot execute SQL for unsupported partition {PartitionId}.", partitionId);
                    throw new InvalidOperationException($"Cannot execute SQL for unsupported partition '{partitionId}'.");
                }

                return new SqlObjectNameFormatter(databaseName);
            });
        }
    }
}
