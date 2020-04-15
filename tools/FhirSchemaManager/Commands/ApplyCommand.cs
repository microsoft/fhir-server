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
        public static async Task HandlerAsync(string connectionString, Uri fhirServer, int version, bool next, bool latest)
        {
            ISchemaClient schemaClient = new SchemaClient(fhirServer);

            var region = new Region(
                          0,
                          0,
                          Console.WindowWidth,
                          Console.WindowHeight,
                          true);

            try
            {
                List<AvailableVersion> availableVersions = await schemaClient.GetAvailability();

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

                availableVersions = availableVersions.Where(availableVersion => availableVersion.Id <= (next == true ?
                                                                                                        availableVersions.First().Id :
                                                                                                        latest == true ?
                                                                                                        availableVersions.Last().Id :
                                                                                                        version))
                    .ToList();

                foreach (AvailableVersion availableVersion in availableVersions)
                {
                    string script = await schemaClient.GetScript(availableVersion.Script);

                    await ValidateVersion(schemaClient, availableVersion.Id);

                    Console.WriteLine(string.Format(Resources.SchemaMigrationStartedMessage, availableVersion.Id));

                    // check if the record for given version exists in failed status
                    SchemaDataStore.ExecuteQuery(connectionString, string.Join(SchemaDataStore.DeleteQuery, availableVersion.Id, SchemaDataStore.Failed), availableVersion.Id);

                    // Execute script
                    SchemaDataStore.ExecuteQuery(connectionString, script, availableVersion.Id);

                    // Update version status to complete state
                    SchemaDataStore.ExecuteUpsertQuery(connectionString, availableVersion.Id, "complete");

                    Console.WriteLine(string.Format(Resources.SchemaMigrationSuccessMessage, availableVersion.Id));
                }
            }
            catch (SchemaManagerException ex)
            {
                CommandUtils.PrintError(ex.Message);
                return;
            }
            catch (HttpRequestException)
            {
                CommandUtils.PrintError(string.Format(Resources.RequestFailedMessage, fhirServer));
                return;
            }
            catch (SqlException ex)
            {
                CommandUtils.PrintError(string.Format(Resources.QueryExecutionExceptionMessage, ex.Message));
                return;
            }
        }

        private static async Task ValidateVersion(ISchemaClient schemaClient, int version)
        {
            CompatibleVersion compatibleVersion = await schemaClient.GetCompatibility();

            // check if version lies in the compatibility range
            if (version < compatibleVersion.Min || version > compatibleVersion.Max)
            {
                throw new SchemaManagerException(string.Join(Resources.VersionIncompatibilityMessage, version));
            }

            List<CurrentVersion> currentVersions = await schemaClient.GetCurrentVersionInformation();

            // check if any instance is not running on the previous version
            if (currentVersions.Any(currentVersion => currentVersion.Id != (version - 1) && currentVersion.Servers.Count > 0))
            {
                throw new SchemaManagerException(string.Join(Resources.InvalidVersionMessage, version));
            }
        }
    }
}
