// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Auth;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Import
{
    public class ImportTestFixture<T> : HttpIntegrationTestFixture<T>
    {
        private const string LocalIntegrationStoreConnectionString = "UseDevelopmentStorage=true";

        public ImportTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
            CloudStorageAccount storageAccount;

            string integrationStoreFromEnvironmentVariable = Environment.GetEnvironmentVariable("TestIntegrationStoreUri");
            string integrationStoreKeyFromEnvironmentVariable = Environment.GetEnvironmentVariable("TestIntegrationStoreKey");
            if (!string.IsNullOrEmpty(integrationStoreFromEnvironmentVariable) && !string.IsNullOrEmpty(integrationStoreKeyFromEnvironmentVariable))
            {
                Uri integrationStoreUri = new Uri(integrationStoreFromEnvironmentVariable);
                string storageAccountName = integrationStoreUri.Host.Split('.')[0];
                StorageCredentials storageCredentials = new StorageCredentials(storageAccountName, integrationStoreKeyFromEnvironmentVariable);
                storageAccount = new CloudStorageAccount(storageCredentials, useHttps: true);
            }
            else
            {
                CloudStorageAccount.TryParse(LocalIntegrationStoreConnectionString, out storageAccount);
            }

            if (storageAccount == null)
            {
                throw new Exception(string.Format("Unable to create a cloud storage account. {0}", integrationStoreFromEnvironmentVariable ?? string.Empty));
            }

            CloudStorageAccount = storageAccount;
        }

        public CloudStorageAccount CloudStorageAccount { get; private set; }

        public string IntegrationStoreConnectionString { get; private set; }
    }
}
