// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data.SqlClient;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.SqlServer.Configs;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema
{
    /// <summary>
    /// EXPERIMENTAL - Initializes the sql schema and brings the schema up to the min supported version.
    /// The purpose of this it to enable easy scenarios during development and will likely be removed later.
    /// </summary>
    public class SchemaInitializer : IStartable
    {
        private readonly SqlServerDataStoreConfiguration _sqlServerDataStoreConfiguration;
        private readonly SchemaUpgradeRunner _schemaUpgradeRunner;
        private readonly SchemaInformation _schemaInformation;
        private readonly ILogger<SchemaInitializer> _logger;

        public SchemaInitializer(SqlServerDataStoreConfiguration sqlServerDataStoreConfiguration, SchemaUpgradeRunner schemaUpgradeRunner, SchemaInformation schemaInformation, ILogger<SchemaInitializer> logger)
        {
            EnsureArg.IsNotNull(sqlServerDataStoreConfiguration, nameof(sqlServerDataStoreConfiguration));
            EnsureArg.IsNotNull(schemaUpgradeRunner, nameof(schemaUpgradeRunner));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlServerDataStoreConfiguration = sqlServerDataStoreConfiguration;
            _schemaUpgradeRunner = schemaUpgradeRunner;
            _schemaInformation = schemaInformation;
            _logger = logger;
        }

        private void Initialize()
        {
            if (!CanInitialize())
            {
                return;
            }

            if (!IsSchemaTableInitialized())
            {
                CreateSchemaTable();
            }

            SetCurrentSchemaVersion();

            if (_schemaInformation.Current == null || _schemaInformation.Current < _schemaInformation.Min)
            {
                int current = (int?)_schemaInformation.Current ?? 0;

                for (int i = current + 1; i <= (int)_schemaInformation.Min; i++)
                {
                    _schemaUpgradeRunner.ApplySchema(i);
                }
            }
        }

        private void CreateSchemaTable()
        {
            using (var connection = new SqlConnection(_sqlServerDataStoreConfiguration.ConnectionString))
            {
                var command = new SqlCommand("CREATE TABLE SchemaTable ( Version int PRIMARY KEY, Status varchar(10) )", connection);

                connection.Open();
                command.ExecuteNonQuery();
            }
        }

        private bool IsSchemaTableInitialized()
        {
            using (var connection = new SqlConnection(_sqlServerDataStoreConfiguration.ConnectionString))
            {
                var command = new SqlCommand("SELECT count(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SchemaTable'", connection);

                connection.Open();
                var tableCount = command.ExecuteScalar() as int?;

                return tableCount == 1;
            }
        }

        private void SetCurrentSchemaVersion()
        {
            using (var connection = new SqlConnection(_sqlServerDataStoreConfiguration.ConnectionString))
            {
                var command = new SqlCommand("SELECT MAX(Version) FROM SchemaTable WHERE Status = 'complete'", connection);

                connection.Open();
                int? version = null;

                try
                {
                    version = command.ExecuteScalar() as int?;
                }
                catch (SqlException ex)
                {
                    _logger.LogWarning(ex, "Could not retrieve the version from the database.");
                }

                _schemaInformation.Current = (SchemaVersion?)version;
            }
        }

        private bool CanInitialize()
        {
            bool canInitialize = _sqlServerDataStoreConfiguration.Initialize;

            if (canInitialize)
            {
                using (var connection = new SqlConnection(_sqlServerDataStoreConfiguration.ConnectionString))
                {
                    var command = new SqlCommand("SELECT count(*) FROM fn_my_permissions (NULL, 'DATABASE') WHERE permission_name = 'CREATE TABLE'", connection);

                    connection.Open();
                    var rowCount = command.ExecuteScalar() as int?;

                    canInitialize = rowCount > 0;
                }
            }

            return canInitialize;
        }

        public void Start()
        {
            if (!string.IsNullOrWhiteSpace(_sqlServerDataStoreConfiguration.ConnectionString))
            {
                Initialize();

                SetCurrentSchemaVersion();
            }
            else
            {
                _logger.LogCritical("There was no connection string supplied. Schema initialization can not be completed.");
            }
        }
    }
}
