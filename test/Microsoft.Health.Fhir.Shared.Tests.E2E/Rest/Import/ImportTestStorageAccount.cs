// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Azure.Storage.Blobs;
using Microsoft.Health.Fhir.Tests.Common;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Import
{
    public class ImportTestStorageAccount
    {
        public ImportTestStorageAccount()
        {
            InitializeFromEnvironmentVariables();
        }

        public Uri StorageUri { get; private set; }

        public BlobServiceClient BlobServiceClient { get; private set; }

        private void InitializeFromEnvironmentVariables()
        {
            StorageUri = new Uri(EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.TestIntegrationStoreUri));
            BlobServiceClient = AzureStorageBlobHelper.GetBlobServiceClient(StorageUri);
        }
    }
}
