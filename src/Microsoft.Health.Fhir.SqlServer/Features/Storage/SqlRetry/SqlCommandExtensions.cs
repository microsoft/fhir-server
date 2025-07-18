// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.SqlServer.Features.Client;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public static class SqlCommandExtensions
    {
        /// <summary>
        /// Logs the parameter declarations and command text of a SQL command
        /// </summary>
        [Conditional("DEBUG")]
        public static void LogSqlCommand(SqlCommand sqlCommand, ILogger logger)
        {
            // TODO: when SqlCommandWrapper is fully deprecated everywhere, modify LogSqlCommand to accept sqlCommand.
            using SqlCommandWrapper sqlCommandWrapper = new SqlCommandWrapper(sqlCommand);
            var sb = new StringBuilder();
            if (sqlCommandWrapper.CommandType == CommandType.Text)
            {
                foreach (SqlParameter p in sqlCommandWrapper.Parameters)
                {
                    sb.Append("DECLARE ")
                        .Append(p)
                        .Append(' ')
                        .Append(p.SqlDbType.ToString().ToLowerInvariant())
                        .Append(p.Value is string ? (p.Size <= 0 ? "(max)" : $"({p.Size})") : p.Value is decimal ? $"({p.Precision},{p.Scale})" : null)
                        .Append(" = ")
                        .Append(p.SqlDbType == SqlDbType.NChar || p.SqlDbType == SqlDbType.NText || p.SqlDbType == SqlDbType.NVarChar ? "N" : null)
                        .AppendLine(p.Value is string || p.Value is DateTime ? $"'{p.Value:O}'" : (p.Value == null ? "NULL" : p.Value.ToString()));
                }

                sb.AppendLine();
                sb.AppendLine(sqlCommandWrapper.CommandText);

                // this just assures that the call to this fn has occurred after the CommandText is set
                Debug.Assert(sqlCommandWrapper.CommandText.Length > 0);
            }
            else
            {
                sb.Append(sqlCommandWrapper.CommandText + string.Empty);
                foreach (SqlParameter p in sqlCommandWrapper.Parameters)
                {
                    sb.Append(p.Value is string || p.Value is DateTime ? $"'{p.Value:O}'" : (p.Value == null ? "NULL" : $"'{p.Value}'"));
                    if (!(sqlCommandWrapper.Parameters.IndexOf(p) == sqlCommandWrapper.Parameters.Count - 1))
                    {
                        sb.Append(", ");
                    }
                }

                sb.AppendLine();
            }

            sb.AppendLine("OPTION (RECOMPILE)"); // enables query compilation with provided parameter values in debugging
            sb.AppendLine($"-- execution timeout = {sqlCommandWrapper.CommandTimeout} sec.");
            logger.LogInformation("{SqlQuery}", sb.ToString());
        }

        public static async Task ExecuteNonQueryAsync(this SqlCommand cmd, ISqlRetryService retryService, ILogger logger, CancellationToken cancellationToken, string logMessage = null, bool isReadOnly = false, bool disableRetries = false, string applicationName = null)
        {
            LogSqlCommand(cmd, logger);
            await retryService.ExecuteSql(cmd, async (sql, cancel) => await sql.ExecuteNonQueryAsync(cancel), logger, logMessage, cancellationToken, isReadOnly, disableRetries, applicationName);
        }

        public static async Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(this SqlCommand cmd, ISqlRetryService retryService, Func<SqlDataReader, TResult> readerToResult, ILogger logger, CancellationToken cancellationToken, string logMessage = null, bool isReadOnly = false)
        {
            LogSqlCommand(cmd, logger);
            return await retryService.ExecuteReaderAsync(cmd, readerToResult, logger, logMessage, cancellationToken, isReadOnly);
        }

        public static async Task<object> ExecuteScalarAsync(this SqlCommand cmd, ISqlRetryService retryService, ILogger logger, CancellationToken cancellationToken, string logMessage = null, bool isReadOnly = false, bool disableRetries = false)
        {
            LogSqlCommand(cmd, logger);
            object scalar = null;
            await retryService.ExecuteSql(cmd, async (sql, cancel) => { scalar = await sql.ExecuteScalarAsync(cancel); }, logger, logMessage, cancellationToken, isReadOnly, disableRetries);
            return scalar;
        }
    }
}
