// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ResourceProcessorNamespace;

namespace FHIRDataSynth
{
    internal sealed class BlobResourceProcessor : ResourceProcessor
    {
        private const string OutputBlobContainerNamePrefix = "blend-";
        private string inputConnectionString;
        private string inputBlobContainerName;
        private string outputConnectionString;
        private string outputBlobContainerName;

        public BlobResourceProcessor(string inputConnectionString, string inputBlobContainerName, string outputConnectionString, string outputBlobContainerName)
        {
            if (inputBlobContainerName == outputBlobContainerName)
            {
                throw new ArgumentException($"Input blob container name '{inputBlobContainerName}' is same as output blob container name '{outputBlobContainerName}'!");
            }

            this.inputConnectionString = inputConnectionString;
            this.inputBlobContainerName = inputBlobContainerName;
            this.outputConnectionString = outputConnectionString;
            this.outputBlobContainerName = null;
            if (outputBlobContainerName != null)
            {
                this.outputBlobContainerName = OutputBlobContainerNamePrefix + outputBlobContainerName;
            }
        }

        protected override void LogInfo(string message)
        {
            Console.WriteLine($"INFO: {message}");
        }

        protected override void LogError(string message)
        {
            Console.WriteLine($"ERROR: {message}");
        }

        protected async override Task<SortedSet<string>> GetResourceGroupDirsAsync()
        {
            return await GetResourceGroupDirsAsync(inputConnectionString, inputBlobContainerName);
        }

        public static async Task<SortedSet<string>> GetResourceGroupDirsAsync(string inputConnectionString, string inputBlobContainerName)
        {
            SortedSet<string> ret = new SortedSet<string>();
            BlobServiceClient blobServiceClient = new BlobServiceClient(inputConnectionString);
            BlobContainerClient blobContainerClient = blobServiceClient.GetBlobContainerClient(inputBlobContainerName);
            await foreach (BlobHierarchyItem blobHierarchyItem in blobContainerClient.GetBlobsByHierarchyAsync(delimiter: "/"))
            {
                ret.Add(blobHierarchyItem.Prefix);
            }

            return ret;
        }

        protected override ResourceGroupProcessor GetNewResourceGroupProcessor(string resourceGroupDir)
        {
            return new BlobResourceGroupProcessor(inputConnectionString, inputBlobContainerName, resourceGroupDir, outputConnectionString, outputBlobContainerName);
        }
    }
}
