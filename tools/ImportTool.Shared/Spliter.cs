// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Logging;

namespace ImportTool.Shared
{
    public static class Spliter
    {
        private static ILogger _logger = GetLogger();

        private static ILogger GetLogger()
        {
            using (var factory = LoggerFactory.Create(builder => builder.AddConsole()))
            {
                return factory.CreateLogger(typeof(Program).FullName);
            }
        }

        public static async Task Split(
            string account,
            string key,
            string prefix,
            long sizeInBytes,
            int maxConcurrentSplitCount,
            int maxConcurrentCountPerFile,
            int maxConcurrentCountPerBlock)
        {
            string storageConnectionString = "UseDevelopmentStorage=true;";
            if (!(string.IsNullOrEmpty(account) || string.IsNullOrEmpty(key)))
            {
                storageConnectionString = $"DefaultEndpointsProtocol=https;AccountName={account};AccountKey={key}";
            }

            CloudStorageAccount storageAccount;
            try
            {
                if (CloudStorageAccount.TryParse(storageConnectionString, out storageAccount))
                {
                    _logger.LogInformation("Start to split files in storage {0} with prefix {1}", account, prefix);

                    CloudBlobClient cloudBlobClient = storageAccount.CreateCloudBlobClient();

                    int count = await SplitBlob(
                        cloudBlobClient,
                        prefix,
                        sizeInBytes,
                        maxConcurrentSplitCount,
                        maxConcurrentCountPerFile,
                        maxConcurrentCountPerBlock);

                    _logger.LogInformation("Split {0} files in total", count);
                }
                else
                {
                    throw new StorageException(
                        "An invalid connection string.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to split due to", ex.Message);
            }
        }

        private static async Task<int> SplitBlob(
            CloudBlobClient cloudBlobClient,
            string prefix,
            long sizeInBytes,
            int maxConcurrentSplitCount,
            int maxConcurrentCountPerFile,
            int maxConcurrentCountPerBlock)
        {
            int count = 0;
            List<Task> runningTasks = new List<Task>();
            BlobContinuationToken continuationToken = null;
            do
            {
                var segments = await cloudBlobClient.ListBlobsSegmentedAsync(
                      prefix: prefix,
                      useFlatBlobListing: true,
                      blobListingDetails: BlobListingDetails.Metadata,
                      maxResults: 500,
                      currentToken: continuationToken,
                      options: null,
                      operationContext: null);

                foreach (var segment in segments.Results.Cast<CloudBlockBlob>())
                {
                    _logger.LogDebug("Start to split files {0}", segment.Uri);

                    if (runningTasks.Count >= maxConcurrentSplitCount)
                    {
                        _ = await Task.WhenAny(runningTasks.ToArray());
                        runningTasks.RemoveAll(t => t.IsCompleted);
                    }

                    // Create sas token of source file
                    SharedAccessBlobPolicy sasPolicy = new SharedAccessBlobPolicy();
                    sasPolicy.SharedAccessExpiryTime = DateTime.UtcNow.AddHours(1);
                    sasPolicy.Permissions = SharedAccessBlobPermissions.Read;
                    string sasToken = segment.GetSharedAccessSignature(sasPolicy);

                    SingleFileSpliter spliter = new SingleFileSpliter(
                        segment,
                        new Uri($"{segment.Uri}?{sasToken}"),
                        sizeInBytes,
                        maxConcurrentCountPerFile,
                        maxConcurrentCountPerBlock);

                    runningTasks.Add(spliter.Split());
                }
            }
            while (continuationToken != null);

            await Task.WhenAll(runningTasks.ToArray());
            return count;
       }
    }
}
