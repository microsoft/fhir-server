// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Build.Framework;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Parameters;
using Microsoft.Health.SqlServer;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SqlServerParameterStore : IParameterStore
    {
        private readonly Dictionary<string, Tuple<DateTime, Parameter>> _parameters = new Dictionary<string, Tuple<DateTime, Parameter>>();
        private readonly int _cacheExpirationInSeconds = 600;
        private readonly ISqlConnectionBuilder _sqlConnectionBuilder;
        private readonly ILogger<SqlServerParameterStore> _logger;

        public SqlServerParameterStore(
            ISqlConnectionBuilder sqlConnectionBuilder,
            ILogger<SqlServerParameterStore> logger)
        {
            EnsureArg.IsNotNull(sqlConnectionBuilder, nameof(sqlConnectionBuilder));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlConnectionBuilder = sqlConnectionBuilder;
            _logger = logger;
        }

        public async Task<Parameter> GetParameter(string name, CancellationToken cancellationToken)
        {
            if (_parameters.TryGetValue(name, out var value) && value.Item1.AddSeconds(_cacheExpirationInSeconds) > DateTime.UtcNow)
            {
                return value.Item2;
            }

            try
            {
                using var conn = await _sqlConnectionBuilder.GetSqlConnectionAsync(cancellationToken: cancellationToken);
                conn.RetryLogicProvider = null;
                await conn.OpenAsync(cancellationToken);
                using var cmd = new SqlCommand("IF object_id('dbo.Parameters') IS NOT NULL SELECT * FROM dbo.Parameters WHERE Id = @name", conn);
                cmd.Parameters.AddWithValue("@name", name);

                Parameter parameter = new Parameter();
                using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    parameter = new Parameter()
                    {
                        Name = reader.GetString(0),
                        DateValue = await reader.IsDBNullAsync(1, cancellationToken) ? DateTime.MinValue : reader.GetDateTime(1),
                        NumberValue = await reader.IsDBNullAsync(2, cancellationToken) ? 0 : reader.GetDouble(2),
                        LongValue = await reader.IsDBNullAsync(3, cancellationToken) ? 0 : reader.GetInt64(3),
                        CharValue = await reader.IsDBNullAsync(4, cancellationToken) ? null : reader.GetString(4),
                        BooleanValue = await reader.IsDBNullAsync(5, cancellationToken) ? false : reader.GetBoolean(5),
                        UpdatedOn = await reader.IsDBNullAsync(6, cancellationToken) ? DateTime.MinValue : reader.GetDateTime(6),
                        UpdatedBy = await reader.IsDBNullAsync(7, cancellationToken) ? null : reader.GetString(7),
                    };
                }

                _parameters[name] = new Tuple<DateTime, Parameter>(DateTime.UtcNow, parameter);
                return parameter;
            }
            catch (SqlException)
            {
                _logger.LogError("Failed to get parameter {Name}", name);
                throw;
            }
        }

        public void ResetCache()
        {
            _parameters.Clear();
        }

        public Task SetParameter(Parameter parameter, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
