// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Azure.Storage;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Import
{
    public class ImportTestStorageAccount
    {
        private const string TestImportStoreUriEnvironmentVariableName = "TestIntegrationStoreUri";
        private const string TestImportStoreKeyEnvironmentVariableName = "TestIntegrationStoreKey";

        public ImportTestStorageAccount()
        {
            InitializeFromEnvironmentVariables();
        }

        public string ConnectionString { get; private set; }

        public Uri StorageUri { get; private set; }

        public StorageSharedKeyCredential SharedKeyCredential { get; private set; }

        private void InitializeFromEnvironmentVariables()
        {
            (Uri storageUri, StorageSharedKeyCredential credential, string connectionString) = AzureStorageBlobHelper.GetStorageCredentialsFromEnvironmentVariables(
                TestImportStoreUriEnvironmentVariableName,
                TestImportStoreKeyEnvironmentVariableName);
            StorageUri = storageUri;
            SharedKeyCredential = credential;
            ConnectionString = connectionString;
        }
    }
}
