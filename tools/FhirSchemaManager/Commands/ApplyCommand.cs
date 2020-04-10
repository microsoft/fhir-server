// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
        private static IInvokeAPI invokeAPI = new InvokeAPI();

        public static async Task HandlerAsync(string connectionString, Uri fhirServer, int version)
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

            try
            {
                List<AvailableVersion> availableVersions = await invokeAPI.GetAvailability(fhirServer);

                if (availableVersions?.Any() != true || availableVersions.Count == 1)
                {
                    CommandUtils.PrintError(Resources.AvailableVersionsDefaultErrorMessage);
                    return;
                }
                else
                {
                    // Removing the current version
                    availableVersions.RemoveAt(0);
                }

                availableVersions = availableVersions.Where(availableVersion => availableVersion.Id <= version)
                    .ToList();

                foreach (AvailableVersion availableVersion in availableVersions)
                {
                    string script = await invokeAPI.GetScript(availableVersion.Script);

                    await ValidateVersion(fhirServer, availableVersion.Id);

                    // check if the record for given version exists in failed status
                    SchemaDataStore.ExecuteQuery(connectionString, string.Join(SchemaDataStore.DeleteQuery, availableVersion.Id));

                    // Execute script
                    SchemaDataStore.ExecuteQuery(connectionString, script);

                    // Update version status to complete state
                    SchemaDataStore.ExecuteUpsertQuery(connectionString, availableVersion.Id, "complete");

                    Console.WriteLine(string.Format(Resources.SuccessMessage, availableVersion.Id));
                }
            }
            catch (SchemaManagerException ex)
            {
                CommandUtils.PrintError(ex.Message);
                return;
            }
            catch (HttpRequestException)
            {
                CommandUtils.PrintError(Resources.RequestFailedMessage);
                return;
            }
            catch (SqlException ex)
            {
                // update version status to failed state
                SchemaDataStore.ExecuteQuery(connectionString, string.Join(SchemaDataStore.UpdateQuery, version));

                CommandUtils.PrintError(string.Format(Resources.QueryExecutionExceptionMessage, ex.Message));
                return;
            }
        }

        private static async Task ValidateVersion(Uri fhirServer, int version)
        {
            CompatibleVersion compatibleVersion = await invokeAPI.GetCompatibility(fhirServer);

            // check if version lies in the compatibility range
            if (!Enumerable.Range(compatibleVersion.Min, compatibleVersion.Max).Contains(version))
            {
                throw new SchemaManagerException(Resources.VersionIncompatibilityMessage);
            }

            List<CurrentVersion> currentVersions = await invokeAPI.GetCurrentVersionInformation(fhirServer);

            // check if any instance is not running on the previous version
            if (currentVersions.Any(currentVersion => currentVersion.Id != (version - 1) && currentVersion.Servers.Count > 0))
            {
                throw new SchemaManagerException(Resources.InvalidVersionMessage);
            }
        }
    }
}
