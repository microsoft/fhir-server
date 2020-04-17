// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
        public static async Task HandlerAsync(string connectionString, Uri fhirServer, int version, bool next)
        {
            ISchemaClient schemaClient = new SchemaClient(fhirServer);

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

                availableVersions = availableVersions.Where(availableVersion => availableVersion.Id <= (next == true ? availableVersions.First().Id : version))
                    .ToList();

                foreach (AvailableVersion availableVersion in availableVersions)
                {
                    string script = await schemaClient.GetScript(availableVersion.Script);

                    await ValidateVersion(schemaClient, availableVersion.Id);

                    // check if the record for given version exists in failed status
                    SchemaDataStore.ExecuteDelete(connectionString, availableVersion.Id, SchemaDataStore.Failed);

                    // Execute script
                    SchemaDataStore.ExecuteQuery(connectionString, script, availableVersion.Id);

                    SchemaDataStore.ExecuteUpsert(connectionString, availableVersion.Id, SchemaDataStore.Complete);

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
                CommandUtils.PrintError(string.Format(Resources.RequestFailedMessage, fhirServer));
                return;
            }
            catch (SqlException ex)
            {
                CommandUtils.PrintError(string.Format(Resources.QueryExecutionErrorMessage, ex.Message));
                return;
            }
        }

        private static async Task ValidateVersion(ISchemaClient schemaClient, int version)
        {
            // to ensure server side polling is completed
            await Task.Delay(60000);

            CompatibleVersion compatibleVersion = await schemaClient.GetCompatibility();

            // check if version doesn't lies in the compatibility range
            if (version < compatibleVersion.Min || version > compatibleVersion.Max)
            {
                throw new SchemaManagerException(Resources.VersionIncompatibilityMessage);
            }

            List<CurrentVersion> currentVersions = await schemaClient.GetCurrentVersionInformation();

            // check if any instance is not running on the previous version
            if (currentVersions.Any(currentVersion => currentVersion.Id != (version - 1) && currentVersion.Servers.Count > 0))
            {
                throw new SchemaManagerException(Resources.InvalidVersionMessage);
            }
        }
    }
}
