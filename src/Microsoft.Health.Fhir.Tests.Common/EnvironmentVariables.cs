// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Tests.Common
{
    public static class EnvironmentVariables
    {
        public const string CosmosDbEmulatorHost = "https://localhost:8081";
        public const string CosmosDbEmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
        public const string DefaultCosmosDbId = "FhirTests";
        public const string LocalSqlConnectionString = "server=(local);Integrated Security=true;TrustServerCertificate=True";
        public const string MasterDatabaseName = "master";
        public const string StorageEmulatorUri = "http://127.0.0.1:10000/devstoreaccount1";

        private static readonly IDictionary<string, string> DefaultValueMap = new Dictionary<string, string>
        {
            { KnownEnvironmentVariableNames.AuthorizationResource, string.Empty },
            { KnownEnvironmentVariableNames.AuthorizationScope, string.Empty },
            { KnownEnvironmentVariableNames.AzureSubscriptionClientId, string.Empty },
            { KnownEnvironmentVariableNames.AzureSubscriptionServiceConnectionId, string.Empty },
            { KnownEnvironmentVariableNames.AzureSubscriptionTenantId, string.Empty },
            { KnownEnvironmentVariableNames.CosmosDbDatabaseId, DefaultCosmosDbId },
            { KnownEnvironmentVariableNames.CosmosDbHost, CosmosDbEmulatorHost },
            { KnownEnvironmentVariableNames.CosmosDbKey, CosmosDbEmulatorKey },
            { KnownEnvironmentVariableNames.CosmosDbPreferredLocations, string.Empty },
            { KnownEnvironmentVariableNames.CosmosDbUseManagedIdentity, string.Empty },
            { KnownEnvironmentVariableNames.SqlServerConnectionString, LocalSqlConnectionString },
            { KnownEnvironmentVariableNames.SystemAccessToken, string.Empty },
            { KnownEnvironmentVariableNames.TestContainerRegistryPassword, string.Empty },
            { KnownEnvironmentVariableNames.TestContainerRegistryServer, string.Empty },
            { KnownEnvironmentVariableNames.TestEnvironmentName, string.Empty },
            { KnownEnvironmentVariableNames.TestEnvironmentUrl, string.Empty },
            { KnownEnvironmentVariableNames.TestExportStoreUri, StorageEmulatorUri },
            { KnownEnvironmentVariableNames.TestIntegrationStoreUri, StorageEmulatorUri },
        };

        public static string GetEnvironmentVariable(string environmentVariableName, string defaultValue = default)
        {
            var environmentVariable = Environment.GetEnvironmentVariable(environmentVariableName);
            if (!string.IsNullOrWhiteSpace(environmentVariable))
            {
                return environmentVariable;
            }

            if (DefaultValueMap.TryGetValue(environmentVariableName, out environmentVariable) && !string.IsNullOrWhiteSpace(environmentVariable))
            {
                return environmentVariable;
            }

            return defaultValue;
        }
    }
}
