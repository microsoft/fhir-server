// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using ResourceProcessorNamespace;

namespace FHIRDataSynth
{
    internal class BlobResourceGroupProcessor : ResourceGroupProcessor
    {
        private string resourceGroupDir;
        private string outputConnectionString;
        private string outputBlobContainerName;
        private BlobServiceClient inputBlobServiceClient;
        private BlobContainerClient inputBlobContainerClient;
        private BlobServiceClient outputBlobServiceClient;
        private BlobContainerClient outputBlobContainerClient;

        public BlobResourceGroupProcessor(string inputConnectionString, string inputBlobContainerName, string resourceGroupDir, string outputConnectionString, string outputBlobContainerName)
        {
            if (inputBlobContainerName == outputBlobContainerName)
            {
                throw new ArgumentException($"Input blob container name '{inputBlobContainerName}' is same as output blob container name '{outputBlobContainerName}'!");
            }

            this.resourceGroupDir = resourceGroupDir;
            this.outputConnectionString = outputConnectionString;
            this.outputBlobContainerName = outputBlobContainerName;

            inputBlobServiceClient = new BlobServiceClient(inputConnectionString);
            inputBlobContainerClient = inputBlobServiceClient.GetBlobContainerClient(inputBlobContainerName);
            if (outputConnectionString != null && outputBlobContainerName != null)
            {
                outputBlobServiceClient = new BlobServiceClient(outputConnectionString);
                outputBlobContainerClient = outputBlobServiceClient.GetBlobContainerClient(outputBlobContainerName);
            }
        }

        protected override bool OnlyVerifyInput { get => outputConnectionString == null || outputBlobContainerName == null; }

        public override string GetResourceGroupDir()
        {
            return resourceGroupDir;
        }

        protected async override Task MakeOutputResourceGroupDirAsync()
        {
            if (outputBlobContainerClient != null)
            {
                await outputBlobContainerClient.CreateIfNotExistsAsync();
            }
        }

        public override void LogInfo(string resourceGroupDir, string resourceName, string resourceId, string message)
        {
            Console.WriteLine($"INFO: {resourceGroupDir}{resourceName}/{resourceId}: {message}");
        }

        public override void LogWarning(string resourceGroupDir, string resourceName, string resourceId, string message)
        {
            Console.WriteLine($"WARNING: {resourceGroupDir}{resourceName}/{resourceId}: {message}");
        }

        /*protected async override Task<ResourcesReturn> ProcessResourcesStreamAsync<T>(string resourceName, HashSet<string> patients, double dbSyntheaRatio)
        {
            if (inputBlobContainerName == outputBlobContainerName)
            {
                throw new ArgumentException($"Input blob container name '{inputBlobContainerName}' is same as output blob container name '{outputBlobContainerName}'!");
            }
            BlobClient inputBlobClient = inputBlobContainerClient.GetBlobClient(resourceGroupDir + resourceName + RDUtility.ResourcesExtension);
            using (MemoryStream outputStream = new MemoryStream(16 * 1024 * 1024))
            using (StreamWriter outputStreamWriter = new StreamWriter(outputStream, new UTF8Encoding(false), 16 * 1024 * 1024))
            {
                ResourcesReturn ret;
                using (Stream inputStream = await inputBlobClient.OpenReadAsync())
                using (StreamReader inputStreamReader = new StreamReader(inputStream, Encoding.UTF8, false, 1024 * 1024))
                {
                    //ret = new ResourcesReturn();
                    //ret.result = new ResourcesResult(resourceName, 0,0,0,0,0,0);
                    //outputStreamWriter.WriteLine(resourceName);
                    ret = await ProcessResourcesAsync<T>(resourceGroupDir, resourceName, inputStreamReader, outputStreamWriter, patients, dbSyntheaRatio);
                }
                BlobClient outputBlobClient = outputBlobContainerClient.GetBlobClient(resourceGroupDir + resourceName + RDUtility.ResourcesExtension);
                outputStreamWriter.Flush();
                outputStream.Seek(0, SeekOrigin.Begin);
                await outputBlobClient.UploadAsync(outputStream, false);
                return ret;
            }
        }*/
        protected async override Task<StreamReader> GetStreamReader(string resourceName)
        {
            BlobClient inputBlobClient = inputBlobContainerClient.GetBlobClient(resourceGroupDir + resourceName + RDUtility.ResourcesExtension);
            Stream inputStream = await inputBlobClient.OpenReadAsync();
            return new StreamReader(inputStream, Encoding.UTF8, false, 1024 * 1024);
        }

        protected async override Task<StreamWriter> GetStreamWriter(string resourceName)
        {
            string blobName = resourceGroupDir + resourceName + RDUtility.ResourcesExtension;
            var blobClientOptions = new BlobClientOptions();
            BlockBlobClient blobClient = new BlockBlobClient(outputConnectionString, outputBlobContainerName, blobName, blobClientOptions);
            if (await blobClient.ExistsAsync())
            {
                throw new FHIRDataSynthException($"Blob {blobName} already exists in container {outputBlobContainerName}.");
            }

            Stream blobStream = await blobClient.OpenWriteAsync(true, new BlockBlobOpenWriteOptions() { BufferSize = 2 * 1024 * 1024 });
            return new StreamWriter(blobStream, new UTF8Encoding(false), 2 * 1024 * 1024);
        }
    }
}
