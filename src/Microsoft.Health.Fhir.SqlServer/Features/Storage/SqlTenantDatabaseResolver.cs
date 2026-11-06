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
using Microsoft.Health.Fhir.Core.Features.Tenants;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// Resolves the current request's SQL database from the immutable tenant registry.
    /// </summary>
    public sealed class SqlTenantDatabaseResolver
    {
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly ITenantManager _tenantManager;
        private readonly ILogger<SqlTenantDatabaseResolver> _logger;

        /// <summary>
        /// Initializes a request-aware tenant database resolver.
        /// </summary>
        public SqlTenantDatabaseResolver(
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor,
            ITenantManager tenantManager,
            ILogger<SqlTenantDatabaseResolver> logger)
        {
            _requestContextAccessor = EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            _tenantManager = EnsureArg.IsNotNull(tenantManager, nameof(tenantManager));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        /// <summary>
        /// Gets the request tenant's database, or null when tenancy is disabled or the operation is tenant-independent.
        /// </summary>
        public string GetDatabaseName()
        {
            if (_tenantManager.Tenants.Count == 0)
            {
                return null;
            }

            string tenantId = _requestContextAccessor.RequestContext?.TenantId;
            if (tenantId == null)
            {
                return null;
            }

            if (!_tenantManager.Tenants.TryGetValue(tenantId, out TenantInfo tenant))
            {
                _logger.LogError("Cannot execute SQL for unsupported tenant {TenantId}.", tenantId);
                throw new InvalidOperationException($"Cannot execute SQL for unsupported tenant '{tenantId}'.");
            }

            return tenant.DatabaseName;
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
