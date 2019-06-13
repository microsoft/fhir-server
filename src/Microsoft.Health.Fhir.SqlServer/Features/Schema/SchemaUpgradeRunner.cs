// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Reflection;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Configs;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema
{
    public class SchemaUpgradeRunner
    {
        private readonly SqlServerDataStoreConfiguration _sqlServerDataStoreConfiguration;
        private readonly ILogger<SchemaUpgradeRunner> _logger;

        public SchemaUpgradeRunner(SqlServerDataStoreConfiguration sqlServerDataStoreConfiguration, ILogger<SchemaUpgradeRunner> logger)
        {
            EnsureArg.IsNotNull(sqlServerDataStoreConfiguration, nameof(sqlServerDataStoreConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlServerDataStoreConfiguration = sqlServerDataStoreConfiguration;
            _logger = logger;
        }

        public void ApplySchema(int version)
        {
            _logger.LogInformation("Applying schema {version}", version);

            if (version != 1)
            {
                InsertSchemaVersion(version);
            }

            using (var connection = new SqlConnection(_sqlServerDataStoreConfiguration.ConnectionString))
            {
                connection.Open();
                var server = new Server(new ServerConnection(connection));
                server.ConnectionContext.ExecuteNonQuery(GetMigrationScript(version));
            }

            CompleteSchemaVersion(version);

            _logger.LogInformation("Completed applying schema {version}", version);
        }

        private static string GetMigrationScript(int version)
        {
            string resourceName = $"{typeof(SchemaUpgradeRunner).Namespace}.Migrations.{version}.sql";
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private void InsertSchemaVersion(int schemaVersion)
        {
            UpsertSchemaVersion(schemaVersion, "started");
        }

        private void CompleteSchemaVersion(int schemaVersion)
        {
            UpsertSchemaVersion(schemaVersion, "complete");
        }

        private void UpsertSchemaVersion(int schemaVersion, string status)
        {
            using (var connection = new SqlConnection(_sqlServerDataStoreConfiguration.ConnectionString))
            {
                var upsertCommand = new SqlCommand("dbo.UpsertSchemaVersion", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                };

                upsertCommand.Parameters.AddWithValue("@version", schemaVersion);
                upsertCommand.Parameters.AddWithValue("@status", status);

                connection.Open();
                upsertCommand.ExecuteNonQuery();
            }
        }
    }
}
