// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.CommandLine.Invocation;
using System.CommandLine.Rendering;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FhirSchemaManager.Exceptions;
using FhirSchemaManager.Model;
using FhirSchemaManager.Utils;

namespace FhirSchemaManager.Commands
{
    public static class ApplyCommand
    {
        public static async Task HandlerAsync(InvocationContext invocationContext, string connectionString, Uri fhirServer, int version)
        {
            Console.WriteLine($"--connection-string {connectionString}");
            Console.WriteLine($"--fhir-server {fhirServer}");
            Console.WriteLine($"--version {version}");

            var region = new Region(
                          0,
                          0,
                          Console.WindowWidth,
                          Console.WindowHeight,
                          true);
            string script;
            List<CurrentVersion> currentVersions = null;
            CompatibleVersion compatibleVersion = null;
            IInvokeAPI invokeAPI = new InvokeAPI();

            try
            {
                script = await invokeAPI.GetScript(fhirServer, version);
                compatibleVersion = await invokeAPI.GetCompatibility(fhirServer);

                // check if version lies in the compatibility range
                if (!Enumerable.Range(compatibleVersion.Min, compatibleVersion.Max).Contains(version))
                {
                    CommandUtils.PrintError(Resources.VersionIncompatibilityMessage);
                    return;
                }

                currentVersions = await invokeAPI.GetCurrentVersionInformation(fhirServer);
            }
            catch (SchemaOperationFailedException ex)
            {
                CommandUtils.RenderError(new ErrorDescription((int)ex.StatusCode, ex.Message), invocationContext, region);
                return;
            }
            catch (HttpRequestException)
            {
                CommandUtils.PrintError(Resources.RequestFailedMessage);
                return;
            }

            // check if any instance is not running on the previous version
            if (currentVersions.Any(currentVersion => currentVersion.Id != (version - 1) && currentVersion.Servers.Count > 0))
            {
                CommandUtils.PrintError(Resources.InvalidVersionMessage);
                return;
            }

            // check if the record for given version exists in failed status
            string deleteQuery = $"DELETE FROM dbo.SchemaVersion WHERE Version = {version} AND Status = 'failed'";
            ExecuteQuery(connectionString, deleteQuery);

            try
            {
                // Execute script
                ExecuteQuery(connectionString, script);

                // Update version status to complete statue
                ExecuteUpsertQuery(connectionString, version, "complete");
            }
            catch (SqlException ex)
            {
                // update version status to failed state
                string updateQuery = $"UPDATE dbo.SchemaVersion SET status = 'failed' WHERE Version = {version} AND Status = 'started'";
                ExecuteQuery(connectionString, updateQuery);

                CommandUtils.PrintError(string.Format(Resources.QueryExecutionExceptionMessage, ex.Message));
                return;
            }

            Console.WriteLine($"Schema Migration is completed successfully for the version : {version}");
        }

        private static void ExecuteQuery(string connectionString, string queryString)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                SqlCommand command = connection.CreateCommand();
                command.CommandText = queryString;
                command.CommandType = CommandType.Text;

                command.ExecuteNonQueryAsync();
            }
        }

        private static void ExecuteUpsertQuery(string connectionString, int version, string status)
        {
            using (var connection = new SqlConnection(connectionString))
            {
                var upsertCommand = new SqlCommand("dbo.UpsertSchemaVersion", connection)
                {
                    CommandType = CommandType.StoredProcedure,
                };

                upsertCommand.Parameters.AddWithValue("@version", version);
                upsertCommand.Parameters.AddWithValue("@status", status);

                connection.Open();
                upsertCommand.ExecuteNonQuery();
            }
        }
    }
}
