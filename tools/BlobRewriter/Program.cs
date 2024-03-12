// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#pragma warning disable CA1303
#pragma warning disable CA1867

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Health.Fhir.Store.Utils;

namespace Microsoft.Health.Internal.Fhir.BlobRewriter
{
    public static class Program
    {
        private static readonly string SourceConnectionString = ConfigurationManager.AppSettings["SourceConnectionString"];
        private static readonly string TargetConnectionString = ConfigurationManager.AppSettings["TargetConnectionString"];
        private static readonly string SourceContainerName = ConfigurationManager.AppSettings["SourceContainerName"];
        private static readonly string TargetContainerName = ConfigurationManager.AppSettings["TargetContainerName"];
        private static readonly int Threads = int.Parse(ConfigurationManager.AppSettings["Threads"]);
        private static readonly int ReportingPeriodSec = int.Parse(ConfigurationManager.AppSettings["ReportingPeriodSec"]);
        private static readonly int LinesPerBlob = int.Parse(ConfigurationManager.AppSettings["LinesPerBlob"]);
        private static readonly int SourceBlobs = int.Parse(ConfigurationManager.AppSettings["SourceBlobs"]);
        private static readonly bool WritesEnabled = bool.Parse(ConfigurationManager.AppSettings["WritesEnabled"]);
        private static readonly bool SplitBySize = bool.Parse(ConfigurationManager.AppSettings["SplitBySize"]);
        private static readonly string NameFilter = ConfigurationManager.AppSettings["NameFilter"];
        private static readonly bool AddMeta = bool.Parse(ConfigurationManager.AppSettings["AddMeta"]);
        private static readonly string BundleType = ConfigurationManager.AppSettings["BundleType"];
        private static readonly bool MultiResourceTypes = bool.Parse(ConfigurationManager.AppSettings["MultiResourceTypes"]);
        private static readonly int BlobRangeSize = int.Parse(ConfigurationManager.AppSettings["BlobRangeSize"]);

        public static void Main()
        {
            Console.WriteLine("!!!See App.config for the details!!!");
            var sourceContainer = GetContainer(SourceConnectionString, SourceContainerName);
            var targetContainer = GetContainer(TargetConnectionString, TargetContainerName);
            var gPrefix = $"BlobRewriter.Threads={Threads}.Source={SourceContainerName}{(WritesEnabled ? $".Target={TargetContainerName}" : string.Empty)}";
            Console.WriteLine($"{DateTime.UtcNow:s}: {gPrefix}: Starting...");
            var blobs = WritesEnabled
                      ? sourceContainer.GetBlobs().Where(_ => _.Name.Contains(NameFilter, StringComparison.OrdinalIgnoreCase) && _.Name.EndsWith(".ndjson", StringComparison.OrdinalIgnoreCase)).OrderBy(_ => _.Name).Take(SourceBlobs)
                      : sourceContainer.GetBlobs().Where(_ => _.Name.Contains(NameFilter, StringComparison.OrdinalIgnoreCase)).Take(SourceBlobs);
            if (WritesEnabled)
            {
                Console.WriteLine($"{DateTime.UtcNow:s}: {gPrefix}: SourceBlobs={blobs.Count()}.");
            }

            var sw = Stopwatch.StartNew();
            var swReport = Stopwatch.StartNew();
            var totalLines = 0L;
            var sourceBlobs = 0L;
            var targetBlobs = 0L;
            if (MultiResourceTypes)
            {
                BatchExtensions.ExecuteInParallelBatches(blobs, Threads, BlobRangeSize, (reader, blobInt) =>
                {
                    var directory = GetDirectoryName(blobInt.Item2[0].Name);
                    var sortedLines = GetLinesInBlobGroup(sourceContainer, blobInt.Item2);
                    Interlocked.Add(ref sourceBlobs, blobInt.Item2.Count);
                    BatchExtensions.ExecuteInParallelBatches(sortedLines, Threads, LinesPerBlob, (writer, batch) =>
                    {
                        var index = batch.Item1;
                        var batchOfLnes = batch.Item2;
                        WriteBatch(targetContainer, batchOfLnes, $"{directory}/{(string.IsNullOrEmpty(BundleType) ? "Mixed" : "Bundle")}-{index:000000}.{(string.IsNullOrEmpty(BundleType) ? "ndjson" : "json")}");
                        Interlocked.Increment(ref targetBlobs);
                        Interlocked.Add(ref totalLines, batchOfLnes.Count);
                        if (swReport.Elapsed.TotalSeconds > ReportingPeriodSec)
                        {
                            lock (swReport)
                            {
                                if (swReport.Elapsed.TotalSeconds > ReportingPeriodSec)
                                {
                                    Console.WriteLine($"{DateTime.UtcNow:s}: {gPrefix}: SourceBlobs={sourceBlobs}{(WritesEnabled ? $" TargetBlobs={targetBlobs}" : string.Empty)} Lines={totalLines} secs={(int)sw.Elapsed.TotalSeconds} speed={(int)(totalLines / sw.Elapsed.TotalSeconds)} lines/sec");
                                    swReport.Restart();
                                }
                            }
                        }
                    });
                });
            }
            else
            {
                BatchExtensions.ExecuteInParallelBatches(blobs, Threads, 1, (thread, blobInt) =>
                {
                    var blobIndex = blobInt.Item1;
                    var blob = blobInt.Item2.First();

                    var lines = SplitBySize
                              ? LinesPerBlob == 0 ? CopyBlob(sourceContainer, blob.Name, targetContainer, ref targetBlobs, blobIndex) : SplitBlobBySize(sourceContainer, blob.Name, targetContainer, ref targetBlobs)
                              : SplitBlobByResourceId(sourceContainer, blob.Name, targetContainer, ref targetBlobs);
                    Interlocked.Add(ref totalLines, lines);
                    Interlocked.Increment(ref sourceBlobs);

                    if (swReport.Elapsed.TotalSeconds > ReportingPeriodSec)
                    {
                        lock (swReport)
                        {
                            if (swReport.Elapsed.TotalSeconds > ReportingPeriodSec)
                            {
                                Console.WriteLine($"{DateTime.UtcNow:s}: {gPrefix}: SourceBlobs={sourceBlobs}{(WritesEnabled ? $" TargetBlobs={targetBlobs}" : string.Empty)} Lines={totalLines} secs={(int)sw.Elapsed.TotalSeconds} speed={(int)(totalLines / sw.Elapsed.TotalSeconds)} lines/sec");
                                swReport.Restart();
                            }
                        }
                    }
                });
            }

            Console.WriteLine($"{DateTime.UtcNow:s}: {gPrefix}.Total: SourceBlobs={sourceBlobs}{(WritesEnabled ? $" TargetBlobs={targetBlobs}" : string.Empty)} Lines={totalLines} secs={(int)sw.Elapsed.TotalSeconds} speed={(int)(totalLines / sw.Elapsed.TotalSeconds)} lines/sec");
        }

        private static List<string> GetLinesInBlobGroup(BlobContainerClient sourceContainer, IList<BlobItem> blobs)
        {
            var sw = Stopwatch.StartNew();
            var direct = new List<string>();
            Parallel.ForEach(blobs, new ParallelOptions { MaxDegreeOfParallelism = Threads }, (blob) =>
            {
                var temp = new List<string>();
                foreach (var line in GetLinesInBlob(sourceContainer, blob.Name))
                {
                    temp.Add(line);
                }

                lock (direct)
                {
                    direct.AddRange(temp);
                }
            });
            Console.WriteLine($"{DateTime.UtcNow:s}: Read sourceBlobs={blobs.Count} lines={direct.Count} secs={(int)sw.Elapsed.TotalSeconds} speed={(int)(direct.Count / sw.Elapsed.TotalSeconds)} lines/sec");
            return direct.OrderBy(_ => RandomNumberGenerator.GetInt32((int)1e9)).ToList();
        }

        private static long SplitBlobByResourceId(BlobContainerClient sourceContainer, string blobName, BlobContainerClient targetContainer, ref long targetBlobs)
        {
            var lines = 0L;
            var partitions = new Dictionary<string, List<string>>();
            foreach (var line in GetLinesInBlob(sourceContainer, blobName))
            {
                lines++;
                var partitionKey = GetPartitionKey(line);
                if (!partitions.TryGetValue(partitionKey, out var list))
                {
                    list = new List<string>();
                    partitions.Add(partitionKey, list);
                }

                list.Add(line);
            }

            foreach (var partitionKey in partitions.Keys)
            {
                WriteBatch(targetContainer, partitions[partitionKey], GetTargetBlobName(blobName, partitionKey));
                Interlocked.Increment(ref targetBlobs);
            }

            return lines;
        }

        private static string GetPartitionKey(string jsonString)
        {
            var idStart = jsonString.IndexOf("\"id\":\"", StringComparison.OrdinalIgnoreCase) + 6;
            var firstLetters = jsonString.Substring(idStart, 2);
            if (string.IsNullOrEmpty(firstLetters))
            {
                throw new ArgumentException("Cannot parse resource id with string parser");
            }

            return firstLetters;
        }

        private static long SplitBlobBySize(BlobContainerClient sourceContainer, string blobName, BlobContainerClient targetContainer, ref long targetBlobs)
        {
            var lines = 0L;
            var batch = new List<string>();
            var batchIndex = 0;
            foreach (var line in GetLinesInBlob(sourceContainer, blobName))
            {
                lines++;
                if (WritesEnabled)
                {
                    batch.Add(line);
                    if (batch.Count == LinesPerBlob)
                    {
                        WriteBatch(targetContainer, batch, GetTargetBlobName(blobName, batchIndex));
                        Interlocked.Increment(ref targetBlobs);
                        batch = new List<string>();
                        batchIndex++;
                    }
                }
            }

            if (batch.Count > 0)
            {
                WriteBatch(targetContainer, batch, GetTargetBlobName(blobName, batchIndex));
                Interlocked.Increment(ref targetBlobs);
            }

            return lines;
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

        private static long CopyBlob(BlobContainerClient sourceContainer, string blobName, BlobContainerClient targetContainer, ref long targetBlobs, int blobIndex)
        {
            var lines = 0L;
            using var stream = targetContainer.GetBlockBlobClient(blobName).OpenWrite(true);
            using var writer = new StreamWriter(stream);
            foreach (var line in GetLinesInBlob(sourceContainer, blobName))
            {
                lines++;
                if (WritesEnabled)
                {
                    if (AddMeta)
                    {
                        var date = DateTime.UtcNow.AddMinutes(-SourceBlobs).AddMinutes(blobIndex).AddMilliseconds(lines);
                        var seconds = ((int)(date - DateTime.Parse("1970-01-01")).TotalSeconds).ToString();
                        var lineWithMeta = line.Replace("{\"resourceType\":", "{\"meta\":{\"versionId\":\"" + seconds + "\",\"lastUpdated\":\"" + date.ToString("yyyy-MM-ddTHH:mm:ss.fff") + "\"},\"resourceType\":", StringComparison.OrdinalIgnoreCase);
                        writer.WriteLine(lineWithMeta);
                    }
                    else
                    {
                        writer.WriteLine(line);
                    }
                }
            }

            writer.Flush();

            Interlocked.Increment(ref targetBlobs);

            return lines;
        }

        private static string GetTargetBlobName(string origBlobName, int batchIndex)
        {
            return LinesPerBlob == 0 ? origBlobName : string.IsNullOrEmpty(BundleType) ? $"{origBlobName.Substring(0, origBlobName.Length - 7)}-{batchIndex}.ndjson" : $"{origBlobName.Substring(0, origBlobName.Length - 7)}-{batchIndex}.json";
        }

        private static string GetDirectoryName(string origBlobName)
        {
            return origBlobName.Substring(0, origBlobName.IndexOf('/', StringComparison.OrdinalIgnoreCase));
        }

        private static (string resourceType, string resourceId) ParseJson(string jsonString)
        {
            var idStart = jsonString.IndexOf("\"id\":\"", StringComparison.OrdinalIgnoreCase) + 6;
            var idShort = jsonString.Substring(idStart, 50);
            var idEnd = idShort.IndexOf("\"", StringComparison.OrdinalIgnoreCase);
            var resourceId = idShort.Substring(0, idEnd);
            if (string.IsNullOrEmpty(resourceId))
            {
                throw new ArgumentException("Cannot parse resource id with string parser");
            }

            var rtStart = jsonString.IndexOf("\"resourceType\":\"", StringComparison.OrdinalIgnoreCase) + 16;
            var rtShort = jsonString.Substring(rtStart, 50);
            var rtEnd = rtShort.IndexOf("\"", StringComparison.OrdinalIgnoreCase);
            var resourceType = rtShort.Substring(0, rtEnd);
            if (string.IsNullOrEmpty(resourceType))
            {
                throw new ArgumentException("Cannot parse resource type with string parser");
            }

            return (resourceType, resourceId);
        }

        private static void WriteBatch(BlobContainerClient container, IList<string> batch, string blobName)
        {
            retry:
            try
            {
                using var stream = container.GetBlockBlobClient(blobName).OpenWrite(true);
                using var writer = new StreamWriter(stream);
                if (string.IsNullOrEmpty(BundleType))
                {
                    foreach (var line in batch)
                    {
                        writer.WriteLine(line);
                    }
                }
                else
                {
                    var entries = new List<string>();
                    var keys = new HashSet<(string ResourceType, string ResourceId)>();
                    foreach (var line in batch)
                    {
                        var (resourceType, resourceId) = ParseJson(line);
                        if (keys.Add((resourceType, resourceId))) // there could be dup keys
                        {
                            entries.Add(GetEntry(line, resourceType, resourceId));
                        }
                    }

                    var bundle = GetBundle(entries);
                    writer.Write(bundle);
                }

                writer.Flush();
            }
            catch (Exception e)
            {
                if (e.ToString().Contains("ConditionNotMet", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(e);
                    goto retry;
                }

                throw;
            }
        }

        private static string GetTargetBlobName(string origBlobName, string partition)
        {
            return $"{partition}/{origBlobName}";
        }

        private static IEnumerable<string> GetLinesInBlob(BlobContainerClient container, string blobName)
        {
            using var reader = new StreamReader(container.GetBlobClient(blobName).Download().Value.Content);
            while (!reader.EndOfStream)
            {
                yield return reader.ReadLine();
            }
        }

        private static BlobContainerClient GetContainer(string connectionString, string containerName)
        {
            try
            {
                var blobServiceClient = new BlobServiceClient(connectionString);
                var blobContainerClient = blobServiceClient.GetBlobContainerClient(containerName);

                if (!blobContainerClient.Exists())
                {
                    var container = blobServiceClient.CreateBlobContainer(containerName);
                    Console.WriteLine($"Created container {container.Value.Name}");
                }

                return blobContainerClient;
            }
            catch
            {
                Console.WriteLine($"Unable to parse stroage reference or connect to storage account {connectionString}.");
                throw;
            }
        }
    }
}
