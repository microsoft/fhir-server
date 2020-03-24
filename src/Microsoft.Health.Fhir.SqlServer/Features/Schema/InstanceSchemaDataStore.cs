// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Exceptions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema
{
    public class InstanceSchemaDataStore
    {
        private readonly string _instanceName;
        private readonly SqlServerDataStoreConfiguration _sqlServerDataStoreConfiguration;
        private readonly ILogger<InstanceSchemaDataStore> _logger;

        public InstanceSchemaDataStore(SqlServerDataStoreConfiguration sqlServerDataStoreConfiguration, ILogger<InstanceSchemaDataStore> logger)
        {
            EnsureArg.IsNotNull(sqlServerDataStoreConfiguration, nameof(sqlServerDataStoreConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _instanceName = Guid.NewGuid() + "-" + Process.GetCurrentProcess().Id.ToString();
            _sqlServerDataStoreConfiguration = sqlServerDataStoreConfiguration;
            _logger = logger;
        }

        public string InsertInstanceSchemaInformation(SchemaInformation schemaInformation)
        {
            string output = null;
            using (var connection = new SqlConnection(_sqlServerDataStoreConfiguration.ConnectionString))
            {
                connection.Open();

                string procedureSchema = "dbo";
                string procedureName = "CreateInstanceSchema";

                if (!ProcedureExists(connection, procedureSchema, procedureName))
                {
                    _logger.LogInformation("Procedure to insert the instance schema was not found. This must be a new database.");
                }
                else
                {
                    using (var insertCommand = connection.CreateCommand())
                    {
                        insertCommand.CommandText = $"{procedureSchema}.{procedureName}";
                        insertCommand.CommandType = CommandType.StoredProcedure;

                        insertCommand.Parameters.AddWithValue("@name", _instanceName);
                        insertCommand.Parameters.AddWithValue("@currentVersion", schemaInformation.Current);
                        insertCommand.Parameters.AddWithValue("@maxVersion", schemaInformation.MaximumSupportedVersion);
                        insertCommand.Parameters.AddWithValue("@minVersion", schemaInformation.MinimumSupportedVersion);

                        output = (string)insertCommand.ExecuteScalar();

                        if (output == null)
                        {
                            throw new OperationFailedException(Resources.OperationFailed);
                        }
                    }
                }
            }

            return output;
        }

        public async Task<string> UpsertInstanceSchemaInformation(CompatibleVersions compatibleVersions, int currentVerison)
        {
            string output = null;
            using (var connection = new SqlConnection(_sqlServerDataStoreConfiguration.ConnectionString))
            {
                connection.Open();

                string procedureSchema = "dbo";
                string procedureName = "UpsertInstanceSchema";

                if (!ProcedureExists(connection, procedureSchema, procedureName))
                {
                    _logger.LogInformation("Procedure to upsert the instance schema was not found. This must be a new database.");
                }
                else
                {
                    using (var insertCommand = connection.CreateCommand())
                    {
                        insertCommand.CommandText = $"{procedureSchema}.{procedureName}";
                        insertCommand.CommandType = CommandType.StoredProcedure;

                        insertCommand.Parameters.AddWithValue("@name", _instanceName);
                        insertCommand.Parameters.AddWithValue("@currentVersion", currentVerison);
                        insertCommand.Parameters.AddWithValue("@maxVersion", compatibleVersions.Max);
                        insertCommand.Parameters.AddWithValue("@minVersion", compatibleVersions.Min);

                        output = (string)await insertCommand.ExecuteScalarAsync();

                        if (output == null)
                        {
                            throw new OperationFailedException(Resources.OperationFailed);
                        }
                    }
                }
            }

            return output;
        }

        public async Task DeleteExpiredRecordsAsync()
        {
            using (var connection = new SqlConnection(_sqlServerDataStoreConfiguration.ConnectionString))
            {
                connection.Open();

                string procedureSchema = "dbo";
                string procedureName = "DeleteInstanceSchema";

                if (!ProcedureExists(connection, procedureSchema, procedureName))
                {
                    _logger.LogInformation("Procedure to delete the instance schema was not found. This must be a new database.");
                }
                else
                {
                    using (var deleteCommand = connection.CreateCommand())
                    {
                        deleteCommand.CommandText = $"{procedureSchema}.{procedureName}";
                        deleteCommand.CommandType = CommandType.StoredProcedure;

                        await deleteCommand.ExecuteNonQueryAsync();
                    }
                }
            }
        }

        public async Task<CompatibleVersions> GetCompatibility()
        {
            CompatibleVersions compatibleVersions = null;
            using (var connection = new SqlConnection(_sqlServerDataStoreConfiguration.ConnectionString))
            {
                connection.Open();

                string procedureSchema = "dbo";
                string procedureName = "SelectCompatibleSchemaVersions";

                if (!ProcedureExists(connection, procedureSchema, procedureName))
                {
                    _logger.LogInformation("Procedure to select the compatible versions not found. This must be a new database.");
                }
                else
                {
                    using (var sqlCommand = connection.CreateCommand())
                    {
                        sqlCommand.CommandText = $"{procedureSchema}.{procedureName}";
                        sqlCommand.CommandType = CommandType.StoredProcedure;
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
                    }
                }
            }

            return compatibleVersions;
        }

        public static int ConvertToInt(object o)
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

        private static bool ProcedureExists(SqlConnection connection, string procedureSchema, string procedureName)
        {
            using (var checkProcedureExistsCommand = connection.CreateCommand())
            {
                checkProcedureExistsCommand.CommandText = @"
                        SELECT 1
                        FROM sys.procedures p
                        INNER JOIN sys.schemas s on p.schema_id = s.schema_id
                        WHERE s.name = @schemaName AND p.name = @procedureName";

                checkProcedureExistsCommand.Parameters.AddWithValue("@schemaName", procedureSchema);
                checkProcedureExistsCommand.Parameters.AddWithValue("@procedureName", procedureName);
                return checkProcedureExistsCommand.ExecuteScalar() != null;
            }
        }
    }
}
