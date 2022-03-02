// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Health.Fhir.Store.Utils;
using Newtonsoft.Json.Linq;

namespace Import
{
    internal static class Importer
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private static readonly string ConnectionString = ConfigurationManager.AppSettings["ConnectionString"];
        private static readonly string FhirServiceUrl = ConfigurationManager.AppSettings["FhirServiceUrl"];
        private static readonly string ContainerName = ConfigurationManager.AppSettings["ContainerName"];
        private static readonly int Threads = int.Parse(ConfigurationManager.AppSettings["Threads"]);
        private static readonly int NumberOfBlobsToSkip = int.Parse(ConfigurationManager.AppSettings["NumberOfBlobsToSkip"]);
        private static readonly int NumberOfBlobsToImport = int.Parse(ConfigurationManager.AppSettings["NumberOfBlobsToImport"]);

        internal static void Run()
        {
            ServicePointManager.DefaultConnectionLimit = 2048;

            var blobContainerClient = GetContainer(ConnectionString, ContainerName);
            var currentBlob = NumberOfBlobsToSkip;
            var blobs = blobContainerClient.GetBlobs().Skip(NumberOfBlobsToSkip).Take(NumberOfBlobsToImport - NumberOfBlobsToSkip).ToList();
            const int blobBatchSize = 100; // process 100 blobs as one batch
            var concurrentRequests = 0;
            BatchExtensions.ExecuteInParallelBatches(blobs, 1, blobBatchSize, (t, blobBatchInt) =>
            {
                var blobBatchIndex = blobBatchInt.Item1;
                var blobBatch = blobBatchInt.Item2;
                var logPrefix = $"Threads={Threads}.range=[{NumberOfBlobsToSkip + (blobBatchIndex * blobBatchSize) + 1}-{NumberOfBlobsToSkip + (blobBatchIndex * blobBatchSize) + blobBatch.Count}][{blobBatch.First().Name} {blobBatch.Last().Name}]";
                Console.WriteLine($"Parall={concurrentRequests}.{logPrefix}: Starting...");

                var sw = Stopwatch.StartNew();
                var swReport = Stopwatch.StartNew();
                var resourceCount = 0L;
                var reportInterval = 60;
                BatchExtensions.ExecuteInParallelBatches(GetLinesInBlobs(blobBatch, logPrefix), Threads, 100, (thread, lineBatch) =>
                {
                    foreach (var line in lineBatch.Item2)
                    {
                        Interlocked.Increment(ref resourceCount);
                        PutResource(line, ref concurrentRequests);
                        if (swReport.Elapsed.TotalSeconds > reportInterval)
                        {
                            lock (swReport)
                            {
                                if (swReport.Elapsed.TotalSeconds > reportInterval)
                                {
                                    var res = Interlocked.Read(ref resourceCount);
                                    Console.WriteLine($"Parall={concurrentRequests}.{logPrefix}: Imported resources={res} secs={(int)sw.Elapsed.TotalSeconds} speed={(int)(res / sw.Elapsed.TotalSeconds)} res/sec");
                                    swReport.Restart();
                                }
                            }
                        }
                    }
                });
                Console.WriteLine($"Parall={concurrentRequests}.{logPrefix}: Completed. Imported resources={resourceCount} secs={(int)sw.Elapsed.TotalSeconds} speed={(int)(resourceCount / sw.Elapsed.TotalSeconds)} res/sec");
            });
        }

        private static IEnumerable<string> GetLinesInBlobs(IList<BlobItem> blobs, string logPrefix)
        {
            var blobContainerClient = GetContainer(ConnectionString, ContainerName);
            var totalLines = 0L;
            var sw = Stopwatch.StartNew();
            var swReport = Stopwatch.StartNew();
            var reportInterval = 60;
            var lines = 0L;
            foreach (var blob in blobs)
            {
                if (blob.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using (var reader = new StreamReader(blobContainerClient.GetBlobClient(blob.Name).Download().Value.Content))
                {
                    while (!reader.EndOfStream)
                    {
                        yield return reader.ReadLine();
                        Interlocked.Increment(ref lines);
                        lines++;
                        if (swReport.Elapsed.TotalSeconds > reportInterval)
                        {
                            lock (swReport)
                            {
                                if (swReport.Elapsed.TotalSeconds > reportInterval)
                                {
                                    var res = Interlocked.Read(ref lines);
                                    Console.WriteLine($"{logPrefix}: Read resources={res} secs={(int)sw.Elapsed.TotalSeconds} speed={(int)(res / sw.Elapsed.TotalSeconds)} resources/sec");
                                    swReport.Restart();
                                }
                            }
                        }
                    }
                }
            }

            Console.WriteLine($"{logPrefix}: Read total resources={totalLines} in secs={(int)sw.Elapsed.TotalSeconds}");
        }

        private static BlobContainerClient GetContainer(string connectionString, string containerName)
        {
            try
            {
                var blobServiceClient = new BlobServiceClient(connectionString);
                var blobContainerClient = blobServiceClient.GetBlobContainerClient(containerName);

                if (!blobContainerClient.Exists())
                {
                    Console.WriteLine($"Storage container {containerName} not found.");
                    return null;
                }

                return blobContainerClient;
            }
            catch
            {
                Console.WriteLine($"Unable to parse stroage reference or connect to storage account {connectionString}.");
                throw;
            }
        }

        public static void PutResource(string jsonString, ref int concurrentRequests)
        {
            var jsonObject = JObject.Parse(jsonString);
            if (jsonObject == null || jsonObject["resourceType"] == null)
            {
                throw new ArgumentException($"ndjson file is invalid");
            }

            using var content = new StringContent(jsonString, Encoding.UTF8, "application/json");
            var resourceType = (string)jsonObject["resourceType"];
            var resourceId = (string)jsonObject["id"];
            var uri = new Uri(FhirServiceUrl + "/" + resourceType + "/" + resourceId);
            var bad = false;
            var retries = 0;
            do
            {
                Interlocked.Increment(ref concurrentRequests);
                try
                {
                    var response = HttpClient.PutAsync(uri, content).Result;
                    switch (response.StatusCode)
                    {
                        case HttpStatusCode.OK:
                        case HttpStatusCode.Created:
                            break;
                        default:
                            Console.WriteLine($"Retries={retries} HttpStatusCode={response.StatusCode} ResourceType={resourceType} ResourceId={resourceId}");
                            bad = true;
                            break;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Retries={retries} ResourceType={resourceType} ResourceId={resourceId} Error={e.Message}");
                    bad = true;
                }
                finally
                {
                    Interlocked.Decrement(ref concurrentRequests);
                }

                if (bad && retries < 50)
                {
                    retries++;
                    Thread.Sleep(200 * retries);
                }
            }
            while (bad && retries < 50);
        }
    }
}
