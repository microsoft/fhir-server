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

namespace Microsoft.Health.Fhir.Importer
{
    internal static class Importer
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private static readonly string Endpoints = ConfigurationManager.AppSettings["FhirEndpoints"];
        private static readonly string ConnectionString = ConfigurationManager.AppSettings["ConnectionString"];
        private static readonly string ContainerName = ConfigurationManager.AppSettings["ContainerName"];
        private static readonly int BlobRangeSize = int.Parse(ConfigurationManager.AppSettings["BlobRangeSize"]);
        private static readonly int ReadThreads = int.Parse(ConfigurationManager.AppSettings["ReadThreads"]);
        private static readonly int WriteThreads = int.Parse(ConfigurationManager.AppSettings["WriteThreads"]);
        private static readonly int NumberOfBlobsToSkip = int.Parse(ConfigurationManager.AppSettings["NumberOfBlobsToSkip"]);
        private static readonly int MaxBlobIndexForImport = int.Parse(ConfigurationManager.AppSettings["MaxBlobIndexForImport"]);
        private static readonly int ReportingPeriodSec = int.Parse(ConfigurationManager.AppSettings["ReportingPeriodSec"]);
        private static readonly int MaxRetries = int.Parse(ConfigurationManager.AppSettings["MaxRetries"]);
        private static readonly bool UseStringJsonParser = bool.Parse(ConfigurationManager.AppSettings["UseStringJsonParser"]);
        private static readonly bool UseStringJsonParserCompare = bool.Parse(ConfigurationManager.AppSettings["UseStringJsonParserCompare"]);

        private static long totalReads = 0L;
        private static long readers = 0L;
        private static Stopwatch swReads = Stopwatch.StartNew();
        private static long totalWrites = 0L;
        private static long writers = 0L;
        private static long epCalls = 0L;
        private static long waits = 0L;
        private static List<string> endpoints;

        internal static void Run()
        {
            if (string.IsNullOrEmpty(Endpoints))
            {
                throw new ArgumentException("FhirEndpoints value is empty");
            }

            endpoints = Endpoints.Split(";", StringSplitOptions.RemoveEmptyEntries).ToList();

            var globalPrefix = $"RequestedBlobRange=[{NumberOfBlobsToSkip + 1}-{MaxBlobIndexForImport}]";
            Console.WriteLine($"{globalPrefix}: Starting...");
            var blobContainerClient = GetContainer(ConnectionString, ContainerName);
            var blobs = blobContainerClient.GetBlobs().OrderBy(_ => _.Name).Where(_ => _.Name.EndsWith(".ndjson", StringComparison.OrdinalIgnoreCase)).ToList();
            Console.WriteLine($"Found ndjson blobs={blobs.Count} in {ContainerName}.");
            var take = MaxBlobIndexForImport == 0 ? blobs.Count : MaxBlobIndexForImport - NumberOfBlobsToSkip;
            blobs = blobs.Skip(NumberOfBlobsToSkip).Take(take).ToList();
            var swWrites = Stopwatch.StartNew();
            var swReport = Stopwatch.StartNew();
            var currentBlobRanges = new Tuple<int, int>[ReadThreads];
            BatchExtensions.ExecuteInParallelBatches(blobs, ReadThreads, BlobRangeSize, (reader, blobRangeInt) =>
            {
                var localSw = Stopwatch.StartNew();
                var writes = 0L;
                var blobRangeIndex = blobRangeInt.Item1;
                var blobsInt = blobRangeInt.Item2;
                var firstBlob = NumberOfBlobsToSkip + (blobRangeIndex * BlobRangeSize) + 1;
                var lastBlob = NumberOfBlobsToSkip + (blobRangeIndex * BlobRangeSize) + blobsInt.Count;
                currentBlobRanges[reader] = Tuple.Create(firstBlob, lastBlob);
                var prefix = $"Reader={reader}.BlobRange=[{firstBlob}-{lastBlob}]";
                var incrementor = new IndexIncrementor(endpoints.Count);
                Console.WriteLine($"{prefix}: Starting...");

                // 100 below is a compromise between processing with maximum available threads (value 1) and inefficiency in wrapping single resource in a list.
                BatchExtensions.ExecuteInParallelBatches(GetLinesInBlobRange(blobsInt, prefix), WriteThreads / ReadThreads, 100, (thread, lineBatch) =>
                {
                    Interlocked.Increment(ref writers);
                    foreach (var line in lineBatch.Item2)
                    {
                        Interlocked.Increment(ref totalWrites);
                        Interlocked.Increment(ref writes);
                        PutResource(line, incrementor);
                        if (swReport.Elapsed.TotalSeconds > ReportingPeriodSec)
                        {
                            lock (swReport)
                            {
                                if (swReport.Elapsed.TotalSeconds > ReportingPeriodSec)
                                {
                                    var currWrites = Interlocked.Read(ref totalWrites);
                                    var currReads = Interlocked.Read(ref totalReads);
                                    var minBlob = currentBlobRanges.Min(_ => _.Item1);
                                    var maxBlob = currentBlobRanges.Max(_ => _.Item2);
                                    Console.WriteLine($"{globalPrefix}.WorkingBlobRange=[{minBlob}-{maxBlob}].Readers=[{Interlocked.Read(ref readers)}/{ReadThreads}].Writers=[{Interlocked.Read(ref writers)}].EndPointCalls=[{Interlocked.Read(ref epCalls)}].Waits=[{Interlocked.Read(ref waits)}]: reads={currReads} writes={currWrites} secs={(int)swWrites.Elapsed.TotalSeconds} read-speed={(int)(currReads / swReads.Elapsed.TotalSeconds)} lines/sec write-speed={(int)(currWrites / swWrites.Elapsed.TotalSeconds)} res/sec");
                                    swReport.Restart();
                                }
                            }
                        }
                    }

                    Interlocked.Decrement(ref writers);
                });
                Console.WriteLine($"{prefix}: Completed writes. Total={writes} secs={(int)localSw.Elapsed.TotalSeconds} speed={(int)(writes / localSw.Elapsed.TotalSeconds)} res/sec");
            });
            Console.WriteLine($"{globalPrefix}.Readers=[{readers}/{ReadThreads}].Writers=[{writers}].EndPointCalls=[{epCalls}].Waits=[{waits}]: total reads={totalReads} total writes={totalWrites} secs={(int)swWrites.Elapsed.TotalSeconds} read-speed={(int)(totalReads / swReads.Elapsed.TotalSeconds)} lines/sec write-speed={(int)(totalWrites / swWrites.Elapsed.TotalSeconds)} res/sec");
        }

        private static List<string> GetLinesInBlobRange(IList<BlobItem> blobs, string logPrefix)
        {
            Interlocked.Increment(ref readers);
            swReads.Start(); // just in case it was stopped by decrement logic below

            var lines = 0L;
            var sw = Stopwatch.StartNew();
            var results = new List<string>();
            foreach (var blob in blobs)
            {
                var retries = 0;
                List<string> resInt;
            retry:
                try
                {
                    resInt = new List<string>();
                    using var reader = new StreamReader(GetContainer(ConnectionString, ContainerName).GetBlobClient(blob.Name).Download().Value.Content);
                    while (!reader.EndOfStream)
                    {
                        resInt.Add(reader.ReadLine());
                    }
                }
                catch (Exception e)
                {
                    if (IsNetworkError(e))
                    {
                        Console.WriteLine($"Retries={retries} blob={blob.Name} error={e.Message}");
                        Thread.Sleep(1000);
                        retries++;
                        goto retry;
                    }

                    throw;
                }

                results.AddRange(resInt);
                lines += resInt.Count;
                Interlocked.Add(ref totalReads, resInt.Count);
            }

            if (Interlocked.Decrement(ref readers) == 0)
            {
                swReads.Stop();
            }

            Console.WriteLine($"{logPrefix}: Completed reads. Total={lines} secs={(int)sw.Elapsed.TotalSeconds} speed={(int)(lines / sw.Elapsed.TotalSeconds)} lines/sec");
            return results;
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

        private static void PutResource(string jsonString, IndexIncrementor incrementor)
        {
            var (resourceType, resourceId) = ParseJson(jsonString);
            using var content = new StringContent(jsonString, Encoding.UTF8, "application/json");
            var maxRetries = MaxRetries;
            var retries = 0;
            var networkError = false;
            var bad = false;
            string endpoint;
            do
            {
                endpoint = endpoints[incrementor.Next()];
                var uri = new Uri(endpoint + "/" + resourceType + "/" + resourceId);
                bad = false;
                Interlocked.Increment(ref epCalls);
                try
                {
                    Thread.Sleep(40);
                    var response = HttpClient.PutAsync(uri, content).Result;
                    switch (response.StatusCode)
                    {
                        case HttpStatusCode.OK:
                        case HttpStatusCode.Created:
                            break;
                        default:
                            bad = true;
                            var statusString = response.StatusCode.ToString();
                            if (response.StatusCode != HttpStatusCode.BadGateway || retries > 0) // too many bad gateway messages in the log
                            {
                                Console.WriteLine($"Retries={retries} Endpoint={endpoint} HttpStatusCode={statusString} ResourceType={resourceType} ResourceId={resourceId}");
                            }

                            if (response.StatusCode == HttpStatusCode.TooManyRequests) // retry overload errors forever
                            {
                                maxRetries++;
                            }

                            break;
                    }
                }
                catch (Exception e)
                {
                    networkError = IsNetworkError(e);
                    if (!networkError)
                    {
                        Console.WriteLine($"Retries={retries} Endpoint={endpoint} ResourceType={resourceType} ResourceId={resourceId} Error={(networkError ? "network" : e.Message)}");
                    }

                    bad = true;
                    if (networkError) // retry network errors forever
                    {
                        maxRetries++;
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref epCalls);
                }

                if (bad && retries < maxRetries)
                {
                    retries++;
                    Interlocked.Increment(ref waits);
                    Thread.Sleep(networkError ? 1000 : 200 * retries);
                    Interlocked.Decrement(ref waits);
                }
            }
            while (bad && retries < maxRetries);
            if (bad)
            {
                Console.WriteLine($"Failed writing ResourceType={resourceType} ResourceId={resourceId}. Retries={retries} Endpoint={endpoint}");
            }
        }

        private static bool IsNetworkError(Exception e)
        {
            return e.Message.Contains("connection attempt failed", StringComparison.OrdinalIgnoreCase)
                   || e.Message.Contains("connected host has failed to respond", StringComparison.OrdinalIgnoreCase)
                   || e.Message.Contains("operation on a socket could not be performed", StringComparison.OrdinalIgnoreCase);
        }

        private static (string resourceType, string resourceId) ParseJson(string jsonString)
        {
            if (UseStringJsonParser)
            {
                var idStart = jsonString.IndexOf("\"id\":\"", StringComparison.OrdinalIgnoreCase) + 6;
                var idShort = jsonString.Substring(idStart, 50);
                var idEnd = idShort.IndexOf('"', StringComparison.OrdinalIgnoreCase);
                var resourceId = idShort.Substring(0, idEnd);
                if (string.IsNullOrEmpty(resourceId))
                {
                    throw new ArgumentException("Cannot parse resource id with string parser");
                }

                var rtStart = jsonString.IndexOf("\"resourceType\":\"", StringComparison.OrdinalIgnoreCase) + 16;
                var rtShort = jsonString.Substring(rtStart, 50);
                var rtEnd = rtShort.IndexOf('"', StringComparison.OrdinalIgnoreCase);
                var resourceType = rtShort.Substring(0, rtEnd);
                if (string.IsNullOrEmpty(resourceType))
                {
                    throw new ArgumentException("Cannot parse resource type with string parser");
                }

                if (UseStringJsonParserCompare)
                {
                    var (jResourceType, jResourceId) = ParseJsonWithJObject(jsonString);
                    if (resourceType != jResourceType)
                    {
                        throw new ArgumentException($"{resourceType} != {jResourceType}");
                    }

                    if (resourceId != jResourceId)
                    {
                        throw new ArgumentException($"{resourceId} != {jResourceId}");
                    }
                }

                return (resourceType, resourceId);
            }

            return ParseJsonWithJObject(jsonString);
        }

        private static (string resourceType, string resourceId) ParseJsonWithJObject(string jsonString)
        {
            var jsonObject = JObject.Parse(jsonString);
            if (jsonObject == null || jsonObject["resourceType"] == null)
            {
                throw new ArgumentException($"ndjson file is invalid");
            }

            return ((string)jsonObject["resourceType"], (string)jsonObject["id"]);
        }

        internal sealed class IndexIncrementor
        {
            private int _currentIndex;
            private int _count;

            public IndexIncrementor(int count)
            {
                _count = count;
            }

            public int Next()
            {
                var incremented = Interlocked.Increment(ref _currentIndex);
                var index = incremented % _count;
                if (index < 0)
                {
                    index += _count;
                }

                return index;
            }
        }
    }
}
