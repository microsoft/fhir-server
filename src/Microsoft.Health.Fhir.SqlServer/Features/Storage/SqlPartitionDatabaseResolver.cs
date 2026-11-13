// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.SqlServer.Features.Partitions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// Resolves the current request's SQL database from the immutable partition registry.
    /// </summary>
    public sealed class SqlPartitionDatabaseResolver
    {
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly PartitionManager _partitionManager;
        private readonly ILogger<SqlPartitionDatabaseResolver> _logger;

        /// <summary>
        /// Initializes a request-aware partition database resolver.
        /// </summary>
        public SqlPartitionDatabaseResolver(
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor,
            PartitionManager partitionManager,
            ILogger<SqlPartitionDatabaseResolver> logger)
        {
            _requestContextAccessor = EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            _partitionManager = EnsureArg.IsNotNull(partitionManager, nameof(partitionManager));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        /// <summary>
        /// Gets the request partition's database, or null when partitioning is disabled or the operation is partition-independent.
        /// </summary>
        public string GetDatabaseName()
        {
            if (_partitionManager.Partitions.Count == 0)
            {
                return null;
            }

            string partitionId = _requestContextAccessor.RequestContext?.PartitionId;
            if (partitionId == null)
            {
                return null;
            }

            if (!_partitionManager.TryGetDatabaseName(partitionId, out string databaseName))
            {
                _logger.LogError("Cannot execute SQL for unsupported partition {PartitionId}.", partitionId);
                throw new InvalidOperationException($"Cannot execute SQL for unsupported partition '{partitionId}'.");
            }

            return databaseName;
        }

        /// <summary>
        /// Gets a safely qualified procedure name for the current request.
        /// </summary>
        public string GetStoredProcedureName(string procedureName) =>
            new SqlObjectNameFormatter(GetDatabaseName()).GetStoredProcedureName(procedureName);

        /// <summary>
        /// Qualifies a stored-procedure command without changing text commands or parameters.
        /// </summary>
        public void PrepareCommand(SqlCommand command)
        {
            EnsureArg.IsNotNull(command, nameof(command));
            if (command.CommandType == CommandType.StoredProcedure)
            {
#pragma warning disable CA2100 // Only registry database names and parsed, quoted SQL identifiers are emitted.
                command.CommandText = GetStoredProcedureName(command.CommandText);
#pragma warning restore CA2100
            }
        }
    }
}
