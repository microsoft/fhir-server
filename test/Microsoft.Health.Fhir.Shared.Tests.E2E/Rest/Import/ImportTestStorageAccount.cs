// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Azure.Storage.Blobs;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Import
{
    public class ImportTestStorageAccount
    {
        private const string TestImportStoreUriEnvironmentVariableName = "TestIntegrationStoreUri";

        public ImportTestStorageAccount()
        {
            InitializeFromEnvironmentVariables();
        }

        public Uri StorageUri { get; private set; }

        public BlobServiceClient BlobServiceClient { get; private set; }

        private void InitializeFromEnvironmentVariables()
        {
            StorageUri = new Uri(Environment.GetEnvironmentVariable(TestImportStoreUriEnvironmentVariableName));
            BlobServiceClient = AzureStorageBlobHelper.GetBlobServiceClient(StorageUri);
        }
    }
}
