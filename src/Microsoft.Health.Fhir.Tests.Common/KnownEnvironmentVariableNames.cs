// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Tests.Common
{
    public static class KnownEnvironmentVariableNames
    {
        public const string AuthorizationResource = "Resource";
        public const string AuthorizationScope = "Scope";
        public const string AzureSubscriptionClientId = "AZURESUBSCRIPTION_CLIENT_ID";
        public const string AzureSubscriptionServiceConnectionId = "AZURESUBSCRIPTION_SERVICE_CONNECTION_ID";
        public const string AzureSubscriptionTenantId = "AZURESUBSCRIPTION_TENANT_ID";
        public const string CosmosDbDatabaseId = "CosmosDb__DatabaseId";
        public const string CosmosDbHost = "CosmosDb__Host";
        public const string CosmosDbKey = "CosmosDb__Key";
        public const string CosmosDbPreferredLocations = "CosmosDb__PreferredLocations";
        public const string CosmosDbUseManagedIdentity = "CosmosDb__UseManagedIdentity";
        public const string SqlServerConnectionString = "SqlServer:ConnectionString";
        public const string SystemAccessToken = "SYSTEM_ACCESSTOKEN";
        public const string TestContainerRegistryPassword = "TestContainerRegistryPassword";
        public const string TestContainerRegistryServer = "TestContainerRegistryServer";
        public const string TestEnvironmentName = "TestEnvironmentName";
        public const string TestEnvironmentUrl = "TestEnvironmentUrl";
        public const string TestExportStoreUri = "TestExportStoreUri";
        public const string TestIntegrationStoreUri = "TestIntegrationStoreUri";
    }
}
