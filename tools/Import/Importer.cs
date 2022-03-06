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

namespace Microsoft.Health.Fhir.Import
{
    internal static class Importer
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private static readonly string Endpoint = ConfigurationManager.AppSettings["FhirEndpoint"];
        private static readonly string Endpoint2 = ConfigurationManager.AppSettings["FhirEndpoint2"];
        private static readonly string Endpoint3 = ConfigurationManager.AppSettings["FhirEndpoint3"];
        private static readonly string Endpoint4 = ConfigurationManager.AppSettings["FhirEndpoint4"];
        private static readonly string ConnectionString = ConfigurationManager.AppSettings["ConnectionString"];
        private static readonly string ContainerName = ConfigurationManager.AppSettings["ContainerName"];
        private static readonly int BlobRangeSize = int.Parse(ConfigurationManager.AppSettings["BlobRangeSize"]);
        private static readonly int ReadThreads = int.Parse(ConfigurationManager.AppSettings["ReadThreads"]);
        private static readonly int WriteThreads = int.Parse(ConfigurationManager.AppSettings["WriteThreads"]);
        private static readonly int NumberOfBlobsToSkip = int.Parse(ConfigurationManager.AppSettings["NumberOfBlobsToSkip"]);
        private static readonly int NumberOfBlobsToImport = int.Parse(ConfigurationManager.AppSettings["NumberOfBlobsToImport"]);
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
        private static int numberOfEndpoints = 1;

        internal static void Run()
        {
            if (string.IsNullOrEmpty(Endpoint))
            {
                throw new ArgumentException("FhirEndpoint value is empty");
            }

            if (!string.IsNullOrEmpty(Endpoint2))
            {
                numberOfEndpoints++;
            }

            if (!string.IsNullOrEmpty(Endpoint3))
            {
                numberOfEndpoints++;
            }

            if (!string.IsNullOrEmpty(Endpoint4))
            {
                numberOfEndpoints++;
            }

            var globalPrefix = $"RequestedBlobRange=[{NumberOfBlobsToSkip + 1}-{NumberOfBlobsToImport}]";
            Console.WriteLine($"{globalPrefix}: Starting...");
            var blobContainerClient = GetContainer(ConnectionString, ContainerName);
            var blobs = blobContainerClient.GetBlobs().Skip(NumberOfBlobsToSkip).Take(NumberOfBlobsToImport - NumberOfBlobsToSkip).ToList();
            var swWrites = Stopwatch.StartNew();
            var swReport = Stopwatch.StartNew();
            var swReportReads = Stopwatch.StartNew();
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
                Console.WriteLine($"{prefix}: Starting...");

                BatchExtensions.ExecuteInParallelBatches(GetLinesInBlobRange(blobsInt, prefix), WriteThreads / ReadThreads, 100, (thread, lineBatch) =>
                {
                    Interlocked.Increment(ref writers);
                    foreach (var line in lineBatch.Item2)
                    {
                        Interlocked.Increment(ref totalWrites);
                        Interlocked.Increment(ref writes);
                        PutResource(line, ref writes);
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

        private static IEnumerable<string> GetLinesInBlobRange(IList<BlobItem> blobs, string logPrefix)
        {
            Interlocked.Increment(ref readers);
            swReads.Start(); // just in case it was stopped by decrement logic below

            var blobContainerClient = GetContainer(ConnectionString, ContainerName);
            var lines = 0L;
            var sw = Stopwatch.StartNew();
            foreach (var blob in blobs)
            {
                if (blob.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var reader = new StreamReader(blobContainerClient.GetBlobClient(blob.Name).Download().Value.Content);
                while (!reader.EndOfStream)
                {
                    yield return reader.ReadLine();
                    lines++;
                    Interlocked.Increment(ref totalReads);
                }
            }

            if (Interlocked.Decrement(ref readers) == 0)
            {
                swReads.Stop();
            }

            Console.WriteLine($"{logPrefix}: Completed reads. Total={lines} secs={(int)sw.Elapsed.TotalSeconds} speed={(int)(lines / sw.Elapsed.TotalSeconds)} lines/sec");
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

        private static void PutResource(string jsonString, ref long writes)
        {
            ParseJson(jsonString, out var resourceType, out var resourceId);
            using var content = new StringContent(jsonString, Encoding.UTF8, "application/json");
            var remainder = Interlocked.Read(ref writes) % numberOfEndpoints;
            var fhirEndpoint = remainder == 0 ? Endpoint
                             : remainder == 1 ? Endpoint2
                             : remainder == 2 ? Endpoint3
                             : Endpoint4;
            var uri = new Uri(fhirEndpoint + "/" + resourceType + "/" + resourceId);
            var maxRetries = MaxRetries;
            var retries = 0;
            var networkError = false;
            var bad = false;
            do
            {
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
                            var statusString = response.StatusCode.ToString();
                            Console.WriteLine($"Retries={retries} Endpoint={fhirEndpoint} HttpStatusCode={statusString} ResourceType={resourceType} ResourceId={resourceId}");
                            bad = true;
                            if (statusString == "TooManyRequests") // retry overload errors forever
                            {
                                maxRetries++;
                            }

                            break;
                    }
                }
                catch (Exception e)
                {
                    networkError = e.Message.Contains("connection attempt failed", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("connected host has failed to respond", StringComparison.OrdinalIgnoreCase);
                    Console.WriteLine($"Retries={retries} Endpoint={fhirEndpoint} ResourceType={resourceType} ResourceId={resourceId} Error={(networkError ? "network" : e.Message)}");
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
        }

        private static void ParseJson(string jsonString, out string resourceType, out string resourceId)
        {
            if (UseStringJsonParser)
            {
                var idStart = jsonString.IndexOf(@"""id"":""", StringComparison.OrdinalIgnoreCase) + 6;
                var idShort = jsonString.Substring(idStart, 50);
                var idEnd = idShort.IndexOf(@"""", StringComparison.OrdinalIgnoreCase);
                resourceId = idShort.Substring(0, idEnd);
                if (string.IsNullOrEmpty(resourceId))
                {
                    throw new ArgumentException("Cannot parse resource id with string parser");
                }

                var rtShort = jsonString.Substring(17, 30);
                var rtEnd = rtShort.IndexOf(@"""", StringComparison.OrdinalIgnoreCase);
                resourceType = rtShort.Substring(0, rtEnd);
                if (string.IsNullOrEmpty(resourceType))
                {
                    throw new ArgumentException("Cannot parse resource type with string parser");
                }

                if (UseStringJsonParserCompare)
                {
                    ParseJsonWithJObject(jsonString, out var jResourceType, out var jResourceId);
                    if (resourceType != jResourceType)
                    {
                        throw new ArgumentException($"{resourceType} != {jResourceType}");
                    }

                    if (resourceId != jResourceId)
                    {
                        throw new ArgumentException($"{resourceId} != {jResourceId}");
                    }
                }

                return;
            }

            ParseJsonWithJObject(jsonString, out resourceType, out resourceId);
        }

        private static void ParseJsonWithJObject(string jsonString, out string resourceType, out string resourceId)
        {
            var jsonObject = JObject.Parse(jsonString);
            if (jsonObject == null || jsonObject["resourceType"] == null)
            {
                throw new ArgumentException($"ndjson file is invalid");
            }

            resourceType = (string)jsonObject["resourceType"];
            resourceId = (string)jsonObject["id"];
        }
    }
}
