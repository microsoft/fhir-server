// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data.SqlClient;
using System.IO;
using System.Reflection;
using EnsureThat;
using Microsoft.Health.Fhir.SqlServer.Configs;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema
{
    public class SchemaUpgradeRunner
    {
        private readonly SqlServerDataStoreConfiguration _sqlServerDataStoreConfiguration;

        public SchemaUpgradeRunner(SqlServerDataStoreConfiguration sqlServerDataStoreConfiguration)
        {
            EnsureArg.IsNotNull(sqlServerDataStoreConfiguration, nameof(sqlServerDataStoreConfiguration));

            _sqlServerDataStoreConfiguration = sqlServerDataStoreConfiguration;
        }

        public void ApplySchema(int version)
        {
            using (var connection = new SqlConnection(_sqlServerDataStoreConfiguration.ConnectionString))
            {
                InsertSchemaVersion(version);

                var upgradeCommand = new SqlCommand(GetStringContent(version), connection);

                connection.Open();
                upgradeCommand.ExecuteNonQuery();

                CompleteSchemaVersion(version);
            }
        }

        private static string GetStringContent(int version)
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
            using (var connection = new SqlConnection(_sqlServerDataStoreConfiguration.ConnectionString))
            {
                var upgradeCommand = new SqlCommand($"INSERT INTO SchemaTable (Version, Status) VALUES ({schemaVersion}, 'upgrading')", connection);

                connection.Open();
                upgradeCommand.ExecuteNonQuery();
            }
        }

        private void CompleteSchemaVersion(int schemaVersion)
        {
            using (var connection = new SqlConnection(_sqlServerDataStoreConfiguration.ConnectionString))
            {
                var upgradeCommand = new SqlCommand($"UPDATE SchemaTable SET Status = 'complete' WHERE Version = {schemaVersion}", connection);

                connection.Open();
                upgradeCommand.ExecuteNonQuery();
            }
        }
    }
}
