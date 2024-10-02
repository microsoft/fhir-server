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
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Health.Fhir.Store.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Importer
{
    internal static class Importer
    {
        private static readonly string Endpoints = ConfigurationManager.AppSettings["FhirEndpoints"];
        private static readonly string ConnectionString = ConfigurationManager.AppSettings["ConnectionString"];
        private static readonly string ContainerName = ConfigurationManager.AppSettings["ContainerName"];
        private static readonly int BlobRangeSize = int.Parse(ConfigurationManager.AppSettings["BlobRangeSize"]);
        private static readonly int BatchSize = int.Parse(ConfigurationManager.AppSettings["BatchSize"]);
        private static readonly string BundleType = ConfigurationManager.AppSettings["BundleType"];
        private static readonly bool UseBundleBlobs = bool.Parse(ConfigurationManager.AppSettings["UseBundleBlobs"]);
        private static readonly int ReadThreads = int.Parse(ConfigurationManager.AppSettings["ReadThreads"]);
        private static readonly int WriteThreads = int.Parse(ConfigurationManager.AppSettings["WriteThreads"]);
        private static readonly int NumberOfBlobsToSkip = int.Parse(ConfigurationManager.AppSettings["NumberOfBlobsToSkip"]);
        private static readonly int MaxBlobIndexForImport = int.Parse(ConfigurationManager.AppSettings["MaxBlobIndexForImport"]);
        private static readonly int ReportingPeriodSec = int.Parse(ConfigurationManager.AppSettings["ReportingPeriodSec"]);
        private static readonly int MaxRetries = int.Parse(ConfigurationManager.AppSettings["MaxRetries"]);
        private static readonly bool UseStringJsonParser = bool.Parse(ConfigurationManager.AppSettings["UseStringJsonParser"]);
        private static readonly bool UseStringJsonParserCompare = bool.Parse(ConfigurationManager.AppSettings["UseStringJsonParserCompare"]);
        private static readonly bool UseFhirAuth = bool.Parse(ConfigurationManager.AppSettings["UseFhirAuth"]);
        private static readonly string FhirScopes = ConfigurationManager.AppSettings["FhirScopes"];
        private static readonly string FhirAuthCredentialOptions = ConfigurationManager.AppSettings["FhirAuthCredentialOptions"];

        private static long totalReads = 0L;
        private static long readers = 0L;
        private static Stopwatch swReads = Stopwatch.StartNew();
        private static long totalWrites = 0L;
        private static long writers = 0L;
        private static long epCalls = 0L;
        private static long waits = 0L;
        private static List<string> endpoints;
        private static TokenCredential credential;
        private static HttpClient httpClient = new();
        private static DelegatingHandler handler;

        internal static void Run()
        {
            if (string.IsNullOrEmpty(Endpoints))
            {
                throw new ArgumentException("FhirEndpoints value is empty");
            }

            endpoints = [.. Endpoints.Split(";", StringSplitOptions.RemoveEmptyEntries)];
            SetupAuth();

            var globalPrefix = $"RequestedBlobRange=[{NumberOfBlobsToSkip + 1}-{MaxBlobIndexForImport}]";
            if (BatchSize > 0)
            {
                globalPrefix = globalPrefix + $".Bundle.{BundleType}={BatchSize}";
            }

            Console.WriteLine($"{DateTime.UtcNow:s}: {globalPrefix}: Starting...");
            var blobContainerClient = GetContainer(ConnectionString, ContainerName);
            var blobs = blobContainerClient.GetBlobs().Where(_ => _.Name.EndsWith(UseBundleBlobs ? ".json" : ".ndjson", StringComparison.OrdinalIgnoreCase));
            ////Console.WriteLine($"Found ndjson blobs={blobs.Count} in {ContainerName}.");
            ////var take = MaxBlobIndexForImport == 0 ? blobs.Count : MaxBlobIndexForImport - NumberOfBlobsToSkip;
            blobs = blobs.Skip(NumberOfBlobsToSkip).Take(MaxBlobIndexForImport - NumberOfBlobsToSkip);
            var swWrites = Stopwatch.StartNew();
            var swReport = Stopwatch.StartNew();
            if (UseBundleBlobs)
            {
                var totalBlobs = 0L;
                BatchExtensions.ExecuteInParallelBatches(blobs, WriteThreads, 1, (writer, blobList) =>
                {
                    var incrementor = new IndexIncrementor(endpoints.Count);
                    var bundle = GetTextFromBlob(blobList.Item2.First());
                    PostBundle(bundle, incrementor);
                    Interlocked.Increment(ref totalBlobs);
                    Interlocked.Add(ref totalWrites, BatchSize);

                    if (swReport.Elapsed.TotalSeconds > ReportingPeriodSec)
                    {
                        lock (swReport)
                        {
                            if (swReport.Elapsed.TotalSeconds > ReportingPeriodSec)
                            {
                                var currWrites = Interlocked.Read(ref totalWrites);
                                Console.WriteLine($"{DateTime.UtcNow:s}:{globalPrefix} writers={WriteThreads}: processed blobs={totalBlobs} writes={currWrites} secs={(int)swWrites.Elapsed.TotalSeconds} speed={(int)(currWrites / swWrites.Elapsed.TotalSeconds)} RPS");
                                swReport.Restart();
                            }
                        }
                    }
                });
                Console.WriteLine($"{DateTime.UtcNow:s}:{globalPrefix} writers={WriteThreads}: processed blobs={totalBlobs} total writes={totalWrites} secs={(int)swWrites.Elapsed.TotalSeconds} speed={(int)(totalWrites / swWrites.Elapsed.TotalSeconds)} RPS");
                return;
            }

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
                BatchExtensions.ExecuteInParallelBatches(GetLinesInBlobRange(blobsInt, prefix), WriteThreads / ReadThreads, BatchSize == 0 ? 100 : BatchSize, (thread, lineBatch) =>
                {
                    Interlocked.Increment(ref writers);
                    var batch = new List<string>();
                    foreach (var line in lineBatch.Item2)
                    {
                        if (BatchSize == 0)
                        {
                            PutResource(line, incrementor);
                            Interlocked.Increment(ref totalWrites);
                            Interlocked.Increment(ref writes);
                        }
                        else
                        {
                            batch.Add(line);
                        }

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
                                    Console.WriteLine($"{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff")}:{globalPrefix}.WorkingBlobRange=[{minBlob}-{maxBlob}].Readers=[{Interlocked.Read(ref readers)}/{ReadThreads}].Writers=[{Interlocked.Read(ref writers)}].EndPointCalls=[{Interlocked.Read(ref epCalls)}].Waits=[{Interlocked.Read(ref waits)}]: reads={currReads} writes={currWrites} secs={(int)swWrites.Elapsed.TotalSeconds} read-speed={(int)(currReads / swReads.Elapsed.TotalSeconds)} lines/sec write-speed={(int)(currWrites / swWrites.Elapsed.TotalSeconds)} RPS");
                                    swReport.Restart();
                                }
                            }
                        }
                    }

                    if (BatchSize > 0)
                    {
                        PostBundle(batch, incrementor, ref totalWrites, ref writes);
                    }

                    Interlocked.Decrement(ref writers);
                });
                Console.WriteLine($"{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff")}:{prefix}: Completed writes. Total={writes} secs={(int)localSw.Elapsed.TotalSeconds} speed={(int)(writes / localSw.Elapsed.TotalSeconds)} res/sec");
            });
            Console.WriteLine($"{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff")}:{globalPrefix}.Readers=[{readers}/{ReadThreads}].Writers=[{writers}].EndPointCalls=[{epCalls}].Waits=[{waits}]: total reads={totalReads} total writes={totalWrites} secs={(int)swWrites.Elapsed.TotalSeconds} read-speed={(int)(totalReads / swReads.Elapsed.TotalSeconds)} lines/sec write-speed={(int)(totalWrites / swWrites.Elapsed.TotalSeconds)} RPS");
        }

        private static string GetBundle(IList<string> entries)
        {
            var builder = new StringBuilder();
            builder.Append(@"{""resourceType"":""Bundle"",""type"":""").Append(BundleType).Append(@""",""entry"":[");
            var first = true;
            foreach (var entry in entries)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                builder.Append(entry);
                first = false;
            }

            builder.Append(@"]}");
            return builder.ToString();
        }

        private static string GetEntry(string jsonString, string resourceType, string resourceId)
        {
            var builder = new StringBuilder();
            builder.Append('{')
                   .Append(@"""fullUrl"":""").Append(resourceType).Append('/').Append(resourceId).Append('"')
                   .Append(',').Append(@"""resource"":").Append(jsonString)
                   .Append(',').Append(@"""request"":{""method"":""PUT"",""url"":""").Append(resourceType).Append('/').Append(resourceId).Append(@"""}")
                   .Append('}');
            return builder.ToString();
        }

        private static void PostBundle(string bundle, IndexIncrementor incrementor)
        {
            var maxRetries = MaxRetries;
            var retries = 0;
            var networkError = false;
            var bad = false;
            string endpoint;
            do
            {
                endpoint = endpoints[incrementor.Next()];
                var uri = new Uri(endpoint);
                bad = false;
                try
                {
                    var sw = Stopwatch.StartNew();
                    using var content = new StringContent(bundle, Encoding.UTF8, "application/json");
                    using var request = new HttpRequestMessage(HttpMethod.Post, uri);
                    request.Headers.Add("x-bundle-processing-logic", "parallel");
                    request.Content = content;

                    var response = httpClient.Send(request);
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                    {
                        Console.WriteLine(response.Content.ReadAsStringAsync().Result);
                    }

                    var status = response.StatusCode.ToString();
                    switch (response.StatusCode)
                    {
                        case HttpStatusCode.OK:
                            break;
                        default:
                            bad = true;
                            if (response.StatusCode != HttpStatusCode.BadGateway || retries > 0) // too many bad gateway messages in the log
                            {
                                Console.WriteLine($"{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff")}: Retries={retries} Endpoint={endpoint} HttpStatusCode={status} elapsed={(int)sw.Elapsed.TotalSeconds} secs.");
                            }

                            if (response.StatusCode == HttpStatusCode.TooManyRequests // retry overload errors forever
                                || response.StatusCode == HttpStatusCode.InternalServerError) // 429 on auth cause internal server error
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
                        Console.WriteLine($"{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff")}: Retries={retries} Endpoint={endpoint} Error={e.Message}");
                    }

                    bad = true;
                    if (networkError) // retry network errors forever
                    {
                        maxRetries++;
                    }
                }

                if (bad && retries < maxRetries)
                {
                    retries++;
                    Thread.Sleep(networkError ? 1000 : 1000 * retries);
                }
            }
            while (bad && retries < maxRetries);
            if (bad)
            {
                Console.WriteLine($"{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff")}: Failed write. Retries={retries} Endpoint={endpoint}");
            }

            return;
        }

        private static void PostBundle(IList<string> jsonStrings, IndexIncrementor incrementor, ref long totWrites, ref long writes)
        {
            var entries = new List<string>();
            var keys = new HashSet<(string ResourceType, string ResourceId)>();
            foreach (var jsonString in jsonStrings)
            {
                var (resourceType, resourceId) = ParseJson(jsonString);
                if (keys.Add((resourceType, resourceId)))
                {
                    entries.Add(GetEntry(jsonString, resourceType, resourceId));
                }
            }

            var bundle = GetBundle(entries);
            var maxRetries = MaxRetries;
            var retries = 0;
            var networkError = false;
            var bad = false;
            string endpoint;
            do
            {
                endpoint = endpoints[incrementor.Next()];
                var uri = new Uri(endpoint);
                bad = false;
                try
                {
                    using var content = new StringContent(bundle, Encoding.UTF8, "application/json");
                    using var request = new HttpRequestMessage(HttpMethod.Post, uri);
                    request.Headers.Add("x-bundle-processing-logic", "parallel");
                    request.Content = content;

                    var response = httpClient.Send(request);
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                    {
                        Console.WriteLine(response.Content.ReadAsStringAsync().Result);
                    }

                    var status = response.StatusCode.ToString();
                    switch (response.StatusCode)
                    {
                        case HttpStatusCode.OK:
                            break;
                        default:
                            bad = true;
                            if (response.StatusCode != HttpStatusCode.BadGateway || retries > 0) // too many bad gateway messages in the log
                            {
                                Console.WriteLine($"{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff")}: Retries={retries} Endpoint={endpoint} HttpStatusCode={status}");
                            }

                            if (response.StatusCode == HttpStatusCode.TooManyRequests // retry overload errors forever
                                || response.StatusCode == HttpStatusCode.InternalServerError) // 429 on auth cause internal server error
                            {
                                maxRetries++;
                            }

                            break;
                    }

                    Interlocked.Add(ref totWrites, keys.Count);
                    Interlocked.Add(ref writes, keys.Count);
                }
                catch (Exception e)
                {
                    networkError = IsNetworkError(e);
                    if (!networkError)
                    {
                        Console.WriteLine($"{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff")}: Retries={retries} Endpoint={endpoint} Error={e.Message}");
                    }

                    bad = true;
                    if (networkError) // retry network errors forever
                    {
                        maxRetries++;
                    }
                }

                if (bad && retries < maxRetries)
                {
                    retries++;
                    Thread.Sleep(networkError ? 1000 : 1000 * retries);
                }
            }
            while (bad && retries < maxRetries);
            if (bad)
            {
                Console.WriteLine($"{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff")}: Failed write. Retries={retries} Endpoint={endpoint}");
            }

            return;
        }

        private static string GetTextFromBlob(BlobItem blob)
        {
            using var reader = new StreamReader(GetContainer(ConnectionString, ContainerName).GetBlobClient(blob.Name).Download().Value.Content);
            var text = reader.ReadToEnd();
            return text;
        }

        private static IEnumerable<string> GetLinesInBlobRange(IList<BlobItem> blobs, string logPrefix)
        {
            Interlocked.Increment(ref readers);
            swReads.Start(); // just in case it was stopped by decrement logic below

            var lines = 0L;
            var sw = Stopwatch.StartNew();
            foreach (var blob in blobs.OrderBy(_ => RandomNumberGenerator.GetInt32(1000))) // shuffle blobs
            {
                using var reader = new StreamReader(GetContainer(ConnectionString, ContainerName).GetBlobClient(blob.Name).Download().Value.Content);
                while (!reader.EndOfStream)
                {
                    Interlocked.Increment(ref totalReads);
                    yield return reader.ReadLine();
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
                    var response = httpClient.PutAsync(uri, content).Result;
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

        private static void SetupAuth()
        {
            if (!UseFhirAuth)
            {
                httpClient.Timeout = TimeSpan.FromMinutes(10);
                return;
            }

            List<string> scopes = [.. FhirScopes.Split(";", StringSplitOptions.RemoveEmptyEntries)];
            if (scopes.Count == 0)
            {
                scopes = endpoints.Select(x => $"{x}/.default").ToList();
            }

            if (scopes.Count != endpoints.Count)
            {
                throw new ArgumentException("FhirScopes and FhirEndpoints must have the same number of values.");
            }

            if (!string.IsNullOrEmpty(FhirAuthCredentialOptions))
            {
                var options = JsonConvert.DeserializeObject<DefaultAzureCredentialOptions>(FhirAuthCredentialOptions);
                credential = new DefaultAzureCredential(options);
            }
            else
            {
                credential = new DefaultAzureCredential();
            }

            handler = new BearerTokenHandler(credential, endpoints.Select(x => new Uri(x)).ToArray(), [.. scopes]);
            httpClient = new HttpClient(handler);
            httpClient.Timeout = TimeSpan.FromMinutes(10);

            foreach (var endpoint in endpoints)
            {
                Console.WriteLine($"Testing auth for endpont {endpoint}");
                Uri testUri = new Uri($"{endpoint}?count=1");
                var testResult = httpClient.GetAsync(testUri).Result;

                if (testResult.StatusCode == HttpStatusCode.Unauthorized)
                {
                    Console.WriteLine($"Status={testResult.StatusCode} Result={testResult.Content.ReadAsStringAsync().Result}");
                    throw new ArgumentException("Auth not configured correctly.");
                }
            }
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
