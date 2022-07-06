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
using System.Text;
using System.Threading;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Health.Fhir.Store.Utils;

namespace BlobReaderWriter
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
        private static readonly bool WriteSingleResource = bool.Parse(ConfigurationManager.AppSettings["WriteSingleResource"]);
        private static readonly int WriteThreadsPerReadThread = int.Parse(ConfigurationManager.AppSettings["WriteThreadsPerReadThread"]);
        private static readonly string NameFilter = ConfigurationManager.AppSettings["NameFilter"];

        public static void Main()
        {
            var sourceContainer = GetContainer(SourceConnectionString, SourceContainerName);
            var targetContainer = GetContainer(TargetConnectionString, TargetContainerName);
            var gPrefix = $"BlobReaderWriter.Threads={Threads}{(WriteSingleResource ? $".WriteThreadsPerReadThread={WriteThreadsPerReadThread}" : string.Empty)}.Source={SourceContainerName}{(WritesEnabled ? $".Target={TargetContainerName}" : string.Empty)}";
            Console.WriteLine($"{gPrefix}: Starting at {DateTime.UtcNow.ToString("s")}...");
            var blobs = WritesEnabled
                      ? sourceContainer.GetBlobs().Where(_ => _.Name.Contains(NameFilter, StringComparison.OrdinalIgnoreCase) && _.Name.EndsWith(".ndjson", StringComparison.OrdinalIgnoreCase)).OrderBy(_ => _.Name).Take(SourceBlobs)
                      : sourceContainer.GetBlobs().Where(_ => _.Name.Contains(NameFilter, StringComparison.OrdinalIgnoreCase));
            Console.WriteLine($"{gPrefix}: SourceBlobs={blobs.Count()} at {DateTime.UtcNow.ToString("s")}.");

            var sw = Stopwatch.StartNew();
            var swReport = Stopwatch.StartNew();
            var totalLines = 0L;
            var sourceBlobs = 0L;
            var targetBlobs = 0L;
            if (WriteSingleResource)
            {
                BatchExtensions.ExecuteInParallelBatches(blobs, Threads, 1, (reader, blobInt) =>
                {
                    var blob = blobInt.Item2.First();
                    BatchExtensions.ExecuteInParallelBatches(GetLinesInBlob(sourceContainer, blob), WriteThreadsPerReadThread, 100, (thread, lineBatch) =>
                    {
                        foreach (var line in lineBatch.Item2)
                        {
                            Interlocked.Increment(ref totalLines);
                            PutResource(line, targetContainer);
                            if (swReport.Elapsed.TotalSeconds > ReportingPeriodSec)
                            {
                                lock (swReport)
                                {
                                    if (swReport.Elapsed.TotalSeconds > ReportingPeriodSec)
                                    {
                                        var currWrites = Interlocked.Read(ref totalLines);
                                        Console.WriteLine($"{gPrefix}: SourceBlobs={sourceBlobs}{(WritesEnabled ? $" Writes={totalLines}" : string.Empty)} Lines={totalLines} secs={(int)sw.Elapsed.TotalSeconds} speed={(int)(totalLines / sw.Elapsed.TotalSeconds)} res/sec");
                                        swReport.Restart();
                                    }
                                }
                            }
                        }
                    });
                    Interlocked.Increment(ref sourceBlobs);
                });
            }
            else
            {
                BatchExtensions.ExecuteInParallelBatches(blobs, Threads, 1, (thread, blobInt) =>
                {
                    var blobIndex = blobInt.Item1;
                    var blob = blobInt.Item2.First();

                    var lines = SplitBySize ? SplitBlobBySize(sourceContainer, blob, targetContainer, ref targetBlobs) : SplitBlobByResourceId(sourceContainer, blob, targetContainer, ref targetBlobs);
                    Interlocked.Add(ref totalLines, lines);
                    Interlocked.Increment(ref sourceBlobs);

                    if (swReport.Elapsed.TotalSeconds > ReportingPeriodSec)
                    {
                        lock (swReport)
                        {
                            if (swReport.Elapsed.TotalSeconds > ReportingPeriodSec)
                            {
                                Console.WriteLine($"{gPrefix}: SourceBlobs={sourceBlobs}{(WritesEnabled ? $" TargetBlobs={targetBlobs}" : string.Empty)} Lines={totalLines} secs={(int)sw.Elapsed.TotalSeconds} speed={(int)(totalLines / sw.Elapsed.TotalSeconds)} lines/sec");
                                swReport.Restart();
                            }
                        }
                    }
                });
            }

            Console.WriteLine($"{gPrefix}.Total: SourceBlobs={sourceBlobs}{(WritesEnabled ? $" TargetBlobs={targetBlobs}" : string.Empty)} Lines={totalLines} secs={(int)sw.Elapsed.TotalSeconds} speed={(int)(totalLines / sw.Elapsed.TotalSeconds)} lines/sec");
        }

        private static void PutResource(string json, BlobContainerClient targetContainer)
        {
            var (resourceType, resourceId) = ParseJson(json);

            if (!WritesEnabled)
            {
                return;
            }

            WriteLine(targetContainer, json, $"{resourceType}_{resourceId}.ndjson");
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

        private static long SplitBlobByResourceId(BlobContainerClient sourceContainer, BlobItem blob, BlobContainerClient targetContainer, ref long targetBlobs)
        {
            var lines = 0L;
            var partitions = new Dictionary<string, List<string>>();
            foreach (var line in GetLinesInBlob(sourceContainer, blob))
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
                WriteBatchOfLines(targetContainer, partitions[partitionKey], GetTargetBlobName(blob.Name, partitionKey));
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

        private static long SplitBlobBySize(BlobContainerClient sourceContainer, BlobItem blob, BlobContainerClient targetContainer, ref long targetBlobs)
        {
            var lines = 0L;
            var batch = new List<string>();
            var batchIndex = 0;
            foreach (var line in GetLinesInBlob(sourceContainer, blob))
            {
                lines++;
                if (WritesEnabled)
                {
                    batch.Add(line);
                    if (batch.Count == LinesPerBlob)
                    {
                        WriteBatchOfLines(targetContainer, batch, GetTargetBlobName(blob.Name, batchIndex));
                        Interlocked.Increment(ref targetBlobs);
                        batch = new List<string>();
                        batchIndex++;
                    }
                }
            }

            if (batch.Count > 0)
            {
                WriteBatchOfLines(targetContainer, batch, GetTargetBlobName(blob.Name, batchIndex));
                Interlocked.Increment(ref targetBlobs);
            }

            return lines;
        }

        private static string GetTargetBlobName(string origBlobName, int batchIndex)
        {
            return $"{origBlobName.Substring(0, origBlobName.Length - 7)}-{batchIndex}.ndjson";
        }

        private static void WriteBatchOfLines(BlobContainerClient container, IList<string> batch, string blobName)
        {
        retry:
            try
            {
                using var stream = container.GetBlockBlobClient(blobName).OpenWrite(true);
                using var writer = new StreamWriter(stream);
                foreach (var line in batch)
                {
                    writer.WriteLine(line);
                }

                writer.Flush();
            }
            catch (Exception e)
            {
                if (e.ToString().Contains("ConditionNotMet", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(e.Message);
                    goto retry;
                }

                throw;
            }
        }

        private static void WriteLine(BlobContainerClient container, string line, string blobName)
        {
        retry:
            try
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(line));
                container.GetBlockBlobClient(blobName).Upload(stream);
            }
            catch (Exception e)
            {
                var eStr = e.ToString();
                if (eStr.Contains("ConditionNotMet", StringComparison.OrdinalIgnoreCase)
                        || eStr.Contains("ServerBusy", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(e.Message);
                    Thread.Sleep(2000);
                    goto retry;
                }

                throw;
            }
        }

        private static string GetTargetBlobName(string origBlobName, string partition)
        {
            return $"{partition}/{origBlobName}";
        }

        private static IEnumerable<string> GetLinesInBlob(BlobContainerClient container, BlobItem blob)
        {
            using var reader = new StreamReader(container.GetBlobClient(blob.Name).Download().Value.Content);
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
