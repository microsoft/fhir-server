// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Exceptions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Messages.Get;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal class SqlServerSchemaDataStore : ISchemaDataStore
    {
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly ILogger<SqlServerSchemaDataStore> _logger;

        public SqlServerSchemaDataStore(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            ILogger<SqlServerSchemaDataStore> logger)
        {
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _logger = logger;
        }

        public async Task<GetCompatibilityVersionResponse> GetLatestCompatibleVersionAsync(CancellationToken cancellationToken)
        {
            CompatibleVersions compatibleVersions;
            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper())
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.SelectCompatibleSchemaVersions.PopulateCommand(sqlCommand);

                using (var dataReader = await sqlCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess))
                {
                    if (dataReader.Read())
                    {
                        compatibleVersions = new CompatibleVersions(ConvertToInt(dataReader.GetValue(0)), ConvertToInt(dataReader.GetValue(1)));
                    }
                    else
                    {
                        throw new RecordNotFoundException(Resources.CompatibilityRecordNotFound);
                    }
                }

                return new GetCompatibilityVersionResponse(compatibleVersions);
            }
        }

        public async Task InsertInstanceSchemaInformation(string name, SchemaInformation schemaInformation, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper())
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.CreateInstanceSchema.PopulateCommand(
                     sqlCommand,
                     name,
                     schemaInformation.Current.GetValueOrDefault(),
                     schemaInformation.MaximumSupportedVersion,
                     schemaInformation.MinimumSupportedVersion);
                try
                {
                    var output = (string)await sqlCommand.ExecuteScalarAsync();

                    if (output.NullIfEmpty() == null)
                    {
                        throw new SqlOperationFailedException(Resources.OperationFailed);
                    }
                }
                catch (SqlException e)
                {
                    _logger.LogError(e, "Error from SQL database on insert");
                    throw;
                }
            }
        }

        public async Task UpsertInstanceSchemaInformation(string name, SchemaInformation schemaInformation, int currentVersion, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper())
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.UpsertInstanceSchema.PopulateCommand(
                     sqlCommand,
                     name,
                     currentVersion,
                     schemaInformation.MaximumSupportedVersion,
                     schemaInformation.MinimumSupportedVersion);
                try
                {
                    var output = (string)await sqlCommand.ExecuteScalarAsync();

                    if (output.NullIfEmpty() == null)
                    {
                        throw new SqlOperationFailedException(Resources.OperationFailed);
                    }
                }
                catch (SqlException e)
                {
                    _logger.LogError(e, "Error from SQL database on Upsert");
                    throw;
                }
            }
        }

        public async Task DeleteExpiredRecords()
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper())
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.DeleteInstanceSchema.PopulateCommand(sqlCommand);
                try
                {
                    await sqlCommand.ExecuteNonQueryAsync();
                }
                catch (SqlException e)
                {
                    _logger.LogError(e, "Error from SQL database on Delete");
                    throw;
                }
            }
        }

        public async Task<GetCurrentVersionResponse> GetCurrentVersionAsync(CancellationToken cancellationToken)
        {
            var currentVersions = new List<CurrentVersionInformation>();
            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper())
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.SelectCurrentVersionsInformation.PopulateCommand(sqlCommand);

                try
                {
                    using (var dataReader = await sqlCommand.ExecuteReaderAsync(CommandBehavior.Default))
                    {
                        if (dataReader.HasRows)
                        {
                            while (await dataReader.ReadAsync())
                            {
                                IList<string> instanceNames = new List<string>();
                                if (dataReader.GetValue(2) != null && !Convert.IsDBNull(dataReader.GetValue(2)))
                                {
                                    string names = (string)dataReader.GetValue(2);
                                    instanceNames = names.Split(",").ToList();
                                }

                                var currentVersion = new CurrentVersionInformation((int)dataReader.GetValue(0), (string)dataReader.GetValue(1), instanceNames);
                                currentVersions.Add(currentVersion);
                            }
                        }
                        else
                        {
                            return new GetCurrentVersionResponse(currentVersions);
                        }
                    }
                }
                catch (SqlException e)
                {
                    switch (e.Number)
                    {
                        case SqlErrorCodes.NotFound:
                            throw new RecordNotFoundException(Resources.CurrentRecordNotFound);
                        default:
                            _logger.LogError(e, "Error from SQL database on Select");
                            throw;
                    }
                }
            }

            return new GetCurrentVersionResponse(currentVersions);
        }

        public int? GetCurrentSchemaVersion()
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper())
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.SelectCurrentSchemaVersion.PopulateCommand(sqlCommand);
                try
                {
                    var current = sqlCommand.ExecuteScalar();

                    return (current == null || Convert.IsDBNull(current)) ? null : (int?)current;
                }
                catch (SqlException e)
                {
                    _logger.LogError(e, "Error from SQL database on Get current version");
                    throw;
                }
            }
        }

        private int ConvertToInt(object o)
        {
            if (o == DBNull.Value)
            {
                throw new RecordNotFoundException(Resources.CompatibilityRecordNotFound);
            }
            else
            {
                return Convert.ToInt32(o);
            }
        }
    }
}
