// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// Gives singleton SQL consumers access to the current request's scoped database selection.
    /// </summary>
    public sealed class SqlPartitionDatabaseResolver
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        /// <summary>
        /// Initializes a request-aware partition database resolver.
        /// </summary>
        public SqlPartitionDatabaseResolver(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = EnsureArg.IsNotNull(httpContextAccessor, nameof(httpContextAccessor));
        }

        /// <summary>
        /// Gets the request partition's database, or null when partitioning is disabled or the operation is partition-independent.
        /// </summary>
        public string GetDatabaseName() => GetTarget().DatabaseName;

        /// <summary>
        /// Gets a safely qualified procedure name for the current request.
        /// </summary>
        public string GetStoredProcedureName(string procedureName) =>
            GetTarget().GetStoredProcedureName(procedureName);

        /// <summary>
        /// Qualifies a stored-procedure command without changing text commands or parameters.
        /// </summary>
        public void PrepareCommand(SqlCommand command)
        {
            EnsureArg.IsNotNull(command, nameof(command));
            if (command.CommandType == CommandType.StoredProcedure)
            {
                GetTarget().PrepareCommand(command);
            }
        }

        internal SqlObjectNameFormatter GetTarget() =>
            _httpContextAccessor.HttpContext?.RequestServices.GetRequiredService<SqlRequestDatabase>().GetTarget()
            ?? SqlObjectNameFormatter.ApplicationDatabase;
    }
}
