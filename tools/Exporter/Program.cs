// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Files.DataLake;
using Microsoft.Health.Fhir.Store.Utils;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Store.Export
{
    public static class Program
    {
        private static readonly string SourceConnectionString = ConfigurationManager.ConnectionStrings["SourceDatabase"].ConnectionString;
        private static readonly string QueueConnectionString = ConfigurationManager.ConnectionStrings["QueueDatabase"].ConnectionString;
        private static readonly string BlobConnectionString = ConfigurationManager.AppSettings["BlobConnectionString"];
        private static readonly string BlobContainerName = ConfigurationManager.AppSettings["BlobContainerName"];
        private static readonly int Threads = int.Parse(ConfigurationManager.AppSettings["Threads"]);
        private static readonly string ResourceType = ConfigurationManager.AppSettings["ResourceType"];
        private static readonly int UnitSize = int.Parse(ConfigurationManager.AppSettings["UnitSize"]);
        private static readonly int MaxRetries = int.Parse(ConfigurationManager.AppSettings["MaxRetries"]);
        private static readonly bool ReadsEnabled = bool.Parse(ConfigurationManager.AppSettings["ReadsEnabled"]);
        private static readonly bool WritesEnabled = bool.Parse(ConfigurationManager.AppSettings["WritesEnabled"]);
        private static readonly bool DecompressEnabled = bool.Parse(ConfigurationManager.AppSettings["DecompressEnabled"]);
        private static readonly bool RebuildWorkQueue = bool.Parse(ConfigurationManager.AppSettings["RebuildWorkQueue"]);
        private static readonly int ReportingPeriodSec = int.Parse(ConfigurationManager.AppSettings["ReportingPeriodSec"]);
        private static readonly DateTime StartDate = DateTime.Parse(ConfigurationManager.AppSettings["StartDate"]);
        private static readonly DateTime EndDate = DateTime.Parse(ConfigurationManager.AppSettings["EndDate"]);
        private static readonly SqlService Source = new SqlService(SourceConnectionString);
        private static readonly SqlService Queue = new SqlService(QueueConnectionString);
        private static bool stop = false;
        private static long _resourcesTotal = 0L;
        private static Stopwatch _swReport = Stopwatch.StartNew();
        private static Stopwatch _sw = Stopwatch.StartNew();

        private static Stopwatch _database = new Stopwatch();
        private static Stopwatch _unzip = new Stopwatch();
        private static Stopwatch _blob = new Stopwatch();

        public static void Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "random")
            {
                var count = args.Length > 1 ? int.Parse(args[1]) : 100;
                RandomReads(count);
            }
            else if (args.Length == 0 || args[0] == "storage")
            {
                var count = args.Length > 1 ? int.Parse(args[1]) : 100;
                var bufferKB = args.Length > 2 ? int.Parse(args[2]) : 20;
                var parall = args.Length > 3 ? int.Parse(args[3]) : 1;
                WriteAndReadAdls(count, bufferKB);
                WriteAndReadBlob(count, bufferKB, parall);
            }
            else
            {
                Console.WriteLine($"Source=[{Source.ShowConnectionString()}]");
                if (args.Length > 0 && args[0] == "noqueue")
                {
                    ExportNoQueue();
                }
                else
                {
                    Console.WriteLine($"Queue=[{Queue.ShowConnectionString()}]");
                    if (RebuildWorkQueue)
                    {
                        PopulateJobQueue(ResourceType, UnitSize);
                    }

                    Export();
                }
            }
        }

        public static void RandomReads(int count)
        {
            var ranges = Source.GetSurrogateIdRanges(96, 0, 5104998046807519719, 10000, 1000);
            var refs = new List<(long TransactionId, int OffsetInFile)>();
            foreach (var range in ranges)
            {
                refs.AddRange(Source.GetRefs(96, range.StartId, range.EndId));
            }

            var blobDurations = new List<double>();
            var fileDurations = new List<double>();
            for (var l = 0; l < 10; l++)
            {
                var subSetRefs = refs.OrderBy(_ => RandomNumberGenerator.GetInt32(10000000)).Take(count).ToList();
                var sw = Stopwatch.StartNew();
                var resources = GetRawResourceFromAdls(subSetRefs, true);
                blobDurations.Add(sw.Elapsed.TotalMilliseconds);
                Console.WriteLine($"BLOB.RandomRead.{resources.Count}.buffer={20}.parall=16: total={sw.Elapsed.TotalMilliseconds} msec perLine={sw.Elapsed.TotalMilliseconds / resources.Count} msec");
                sw = Stopwatch.StartNew();
                resources = GetRawResourceFromAdls(subSetRefs, false);
                fileDurations.Add(sw.Elapsed.TotalMilliseconds);
                Console.WriteLine($"ADLS.RandomRead.{resources.Count}.buffer={20}.parall=16: total={sw.Elapsed.TotalMilliseconds} msec perLine={sw.Elapsed.TotalMilliseconds / resources.Count} msec");
            }

            Console.WriteLine($"BLOB.RandomRead.buffer={20}.parall=16: total={blobDurations.Sum() / 10} msec");
            Console.WriteLine($"ADLS.RandomRead.buffer={20}.parall=16: total={fileDurations.Sum() / 10} msec");
        }

        public static IReadOnlyList<string> GetRawResourceFromAdls(IReadOnlyList<(long TransactionId, int OffsetInFile)> resourceRefs, bool isBlob)
        {
            var start = DateTime.UtcNow;
            var results = new List<string>();
            if (resourceRefs == null || resourceRefs.Count == 0)
            {
                return results;
            }

            if (isBlob)
            {
                var container = GetContainer(BlobConnectionString, BlobContainerName);
                Parallel.ForEach(resourceRefs, new ParallelOptions { MaxDegreeOfParallelism = 16 }, (resourceRef) =>
                {
                    var blobName = GetBlobName(resourceRef.TransactionId);
                    var blobClient = container.GetBlobClient(blobName);
                    using var reader = new StreamReader(blobClient.OpenRead(resourceRef.OffsetInFile));
                    var line = reader.ReadLine();
                    lock (results)
                    {
                        results.Add(line.Split('\t')[4]);
                    }
                });
            }
            else
            {
                var resourceRefsByTransaction = resourceRefs.GroupBy(_ => _.TransactionId);
                Parallel.ForEach(resourceRefsByTransaction, new ParallelOptions { MaxDegreeOfParallelism = 16 }, (group) =>
                {
                    var transactionId = group.Key;
                    var blobName = GetBlobName(transactionId);
                    var fileClient = new DataLakeFileClient(BlobConnectionString, BlobContainerName, blobName);
                    using var stream = fileClient.OpenRead(bufferSize: 1024 * 20);
                    using var reader = new StreamReader(stream);
                    foreach (var resourceRef in group.Select(_ => _))
                    {
                        reader.DiscardBufferedData();
                        stream.Position = resourceRef.OffsetInFile;
                        var line = reader.ReadLine();
                        lock (results)
                        {
                            results.Add(line.Split('\t')[4]);
                        }
                    }
                });
            }

            Source.LogEvent("GetRawResourceFromAdls", "Warn", null, rows: (int)(DateTime.UtcNow - start).TotalMilliseconds, text: $"Resources={results.Count}", startTime: start);

            return results;
        }

        private static string GetBlobName(long transactionId)
        {
            return $"transaction-{transactionId}.tjson";
        }

        public static void WriteAndReadAdls(int count, int bufferKB)
        {
            GetContainer(BlobConnectionString, "fhir-hs-new-one-file");

            var fileName = "transaction-353229202.tjson";

            var swGlobal = Stopwatch.StartNew();

            var fileClient = new DataLakeFileClient(BlobConnectionString, "fhir-hs-new-one-file", fileName);

            var offests = new List<int>();
            var offset = 0;
            var eol = Encoding.UTF8.GetByteCount(Environment.NewLine);

            var baseLine = string.Concat(Enumerable.Repeat("0123456789", 200)); // 2KB

            using var writeStream = fileClient.OpenWrite(true);
            using var writer = new StreamWriter(writeStream);
            for (var i = 0; i < count; i++)
            {
                offests.Add(offset);
                var line = $"{offset}\t{baseLine}";
                offset += Encoding.UTF8.GetByteCount(line) + eol;
                writer.WriteLine(line);
            }

            writer.Flush();
            Console.WriteLine($"ADLS.Write.{count}: total={swGlobal.Elapsed.TotalMilliseconds} msec perLine={swGlobal.Elapsed.TotalMilliseconds / count} msec");

            swGlobal = Stopwatch.StartNew();
            fileClient = new DataLakeFileClient(BlobConnectionString, "testadls", fileName);
            using var stream = fileClient.OpenRead(bufferSize: 1024 * bufferKB);
            using var reader = new StreamReader(stream);
            foreach (var pos in offests)
            {
                var sw = Stopwatch.StartNew();
                reader.DiscardBufferedData();
                stream.Position = pos;
                var line = reader.ReadLine();
                var readOffset = line.Split('\t')[0];
                Console.WriteLine($"ADLS.Read.{count}.buffer={bufferKB}: {sw.Elapsed.TotalMilliseconds} msec (input,read)=({pos},{readOffset})");
            }

            Console.WriteLine($"ADLS.Read.{count}.buffer={bufferKB}: total={swGlobal.Elapsed.TotalMilliseconds} msec perLine={swGlobal.Elapsed.TotalMilliseconds / count} msec");
        }

        public static void WriteAndReadBlob(int count, int bufferKB, int parall)
        {
            var fileName = "test/test/test.txt";

            var swGlobal = Stopwatch.StartNew();

            var container = GetContainer(BlobConnectionString, "testblob");

            var offests = new List<int>();
            var offset = 0;
            var eol = Encoding.UTF8.GetByteCount(Environment.NewLine);

            var baseLine = string.Concat(Enumerable.Repeat("0123456789", 200)); // 2KB

            using var writeStream = container.GetBlockBlobClient(fileName).OpenWrite(true);
            using var writer = new StreamWriter(writeStream);
            for (var i = 0; i < count; i++)
            {
                offests.Add(offset);
                var line = $"{offset}\t{baseLine}";
                offset += Encoding.UTF8.GetByteCount(line) + eol;
                writer.WriteLine(line);
            }

            writer.Flush();

            Console.WriteLine($"BLOB.Write.{count}: total={swGlobal.Elapsed.TotalMilliseconds} msec perLine={swGlobal.Elapsed.TotalMilliseconds / count} msec");

            swGlobal = Stopwatch.StartNew();
            container = GetContainer(BlobConnectionString, "testblob");
            var blobClient = container.GetBlobClient(fileName);
            Parallel.ForEach(offests, new ParallelOptions { MaxDegreeOfParallelism = parall }, (pos) =>
            {
                var sw = Stopwatch.StartNew();
                using var readStream = blobClient.OpenRead(pos, bufferSize: 1024 * bufferKB);
                using var reader = new StreamReader(readStream);
                var line = reader.ReadLine();
                var readOffset = line.Split('\t')[0];
                Console.WriteLine($"BLOB.Read.{count}: {sw.Elapsed.TotalMilliseconds} msec (input,read)=({pos},{readOffset})");
            });

            Console.WriteLine($"BLOB.Read.{count}.buffer={bufferKB}.parall={parall}: total={swGlobal.Elapsed.TotalMilliseconds} msec perLine={swGlobal.Elapsed.TotalMilliseconds / count} msec");
        }

        public static void ExportNoQueue()
        {
            var startId = LastUpdatedToResourceSurrogateId(StartDate);
            var endId = LastUpdatedToResourceSurrogateId(EndDate);
            var resourceTypeId = Source.GetResourceTypeId(ResourceType);
            var ranges = Source.GetSurrogateIdRanges(resourceTypeId, startId, endId, UnitSize, (int)(2e9 / UnitSize)).ToList();
            Console.WriteLine($"ExportNoQueue.{ResourceType}: ranges={ranges.Count}.");
            var container = GetContainer(BlobConnectionString, BlobContainerName);
            foreach (var range in ranges)
            {
                Export(resourceTypeId, container, range.StartId, range.EndId);
                if (_swReport.Elapsed.TotalSeconds > ReportingPeriodSec)
                {
                    lock (_swReport)
                    {
                        if (_swReport.Elapsed.TotalSeconds > ReportingPeriodSec)
                        {
                            Console.WriteLine($"ExportNoQueue.{ResourceType}.threads={Threads}.Writes={WritesEnabled}.Decompress={DecompressEnabled}.Reads={ReadsEnabled}: Resources={_resourcesTotal} secs={(int)_sw.Elapsed.TotalSeconds} speed={(int)(_resourcesTotal / _sw.Elapsed.TotalSeconds)} resources/sec DB={_database.Elapsed.TotalSeconds} sec UnZip={_unzip.Elapsed.TotalSeconds} sec Blob={_blob.Elapsed.TotalSeconds}");
                            _swReport.Restart();
                        }
                    }
                }
            }

            Console.WriteLine($"ExportNoQueue.{ResourceType}.threads={Threads}: {(stop ? "FAILED" : "completed")} at {DateTime.Now:s}, resources={_resourcesTotal} speed={_resourcesTotal / _sw.Elapsed.TotalSeconds:N0} resources/sec elapsed={_sw.Elapsed.TotalSeconds:N0} sec DB={_database.Elapsed.TotalSeconds} sec UnZip={_unzip.Elapsed.TotalSeconds} sec Blob={_blob.Elapsed.TotalSeconds}");
        }

        public static void Export()
        {
            var tasks = new List<Task>();
            for (var i = 0; i < Threads; i++)
            {
                var thread = i;
                tasks.Add(BatchExtensions.StartTask(() =>
                {
                    Export(thread);
                }));
            }

            Task.WaitAll(tasks.ToArray());
            Console.WriteLine($"Export.{ResourceType}.threads={Threads}: {(stop ? "FAILED" : "completed")} at {DateTime.Now:s}, resources={_resourcesTotal} speed={_resourcesTotal / _sw.Elapsed.TotalSeconds:N0} resources/sec elapsed={_sw.Elapsed.TotalSeconds:N0} sec DB={_database.Elapsed.TotalSeconds} sec UnZip={_unzip.Elapsed.TotalSeconds} sec Blob={_blob.Elapsed.TotalSeconds}");
        }

        private static void Export(int thread)
        {
            Console.WriteLine($"Export.{thread}: started at {DateTime.UtcNow:s}");
            var resourceTypeId = (short?)0;
            while (resourceTypeId.HasValue && !stop)
            {
                string minId = null;
                string maxId = null;
                var unitId = 0L;
                var version = 0L;
                var retries = 0;
                var maxRetries = MaxRetries;
            retry:
                try
                {
                    Queue.DequeueJob(out var _, out unitId, out version, out resourceTypeId, out minId, out maxId);
                    if (resourceTypeId.HasValue)
                    {
                        var container = GetContainer(BlobConnectionString, BlobContainerName);
                        var resources = Export(resourceTypeId.Value, container, long.Parse(minId), long.Parse(maxId));
                        Queue.CompleteJob(unitId, false, version, resources);
                    }

                    if (_swReport.Elapsed.TotalSeconds > ReportingPeriodSec)
                    {
                        lock (_swReport)
                        {
                            if (_swReport.Elapsed.TotalSeconds > ReportingPeriodSec)
                            {
                                Console.WriteLine($"Export.{ResourceType}.threads={Threads}.Writes={WritesEnabled}.Decompress={DecompressEnabled}.Reads={ReadsEnabled}: Resources={_resourcesTotal} secs={(int)_sw.Elapsed.TotalSeconds} speed={(int)(_resourcesTotal / _sw.Elapsed.TotalSeconds)} resources/sec DB={_database.Elapsed.TotalSeconds} sec UnZip={_unzip.Elapsed.TotalSeconds} sec Blob={_blob.Elapsed.TotalSeconds}");
                                _swReport.Restart();
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Export.{ResourceType}.{thread}.{minId}.{maxId}: error={e}");
                    Queue.LogEvent($"Export", "Error", $"{ResourceType}.{thread}.{minId}.{maxId}", text: e.ToString());
                    retries++;
                    var isRetryable = e.IsRetryable();
                    if (isRetryable)
                    {
                        maxRetries++;
                    }

                    if (retries < maxRetries)
                    {
                        Thread.Sleep(isRetryable ? 1000 : 200 * retries);
                        goto retry;
                    }

                    stop = true;
                    if (resourceTypeId.HasValue)
                    {
                        Queue.CompleteJob(unitId, true, version);
                    }

                    throw;
                }
            }

            ////Console.WriteLine($"Export.{ResourceType}.{thread}: {(stop ? "FAILED" : "completed")} at {DateTime.Now:s}, elapsed={_sw.Elapsed.TotalSeconds:N0} sec.");
        }

        private static int Export(short resourceTypeId, BlobContainerClient container, long minId, long maxId)
        {
            if (!ReadsEnabled)
            {
                return 0;
            }

            _database.Start();
            var resources = Source.GetDataBytes(resourceTypeId, minId, maxId).ToList(); // ToList will fource reading from SQL even when writes are disabled
            _database.Stop();

            var strings = new List<string>();
            if (DecompressEnabled)
            {
                _unzip.Start();
                foreach (var res in resources)
                {
                    using var mem = new MemoryStream(res);
                    strings.Add(CompressedRawResourceConverterCopy.ReadCompressedRawResource(mem));
                }

                _unzip.Stop();
            }

            if (WritesEnabled)
            {
                _blob.Start();
                WriteBatchOfLines(container, strings, $"{ResourceType}-{minId}-{maxId}.ndjson");
                _blob.Stop();
            }

            Interlocked.Add(ref _resourcesTotal, strings.Count);

            return strings.Count;
        }

        private static void WriteBatchOfLines(BlobContainerClient container, IEnumerable<string> batch, string blobName)
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
                    Console.WriteLine(e);
                    goto retry;
                }

                throw;
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
                    lock (_sw) // lock on anything global
                    {
                        blobContainerClient = blobServiceClient.GetBlobContainerClient(containerName);
                        if (!blobContainerClient.Exists())
                        {
                            var container = blobServiceClient.CreateBlobContainer(containerName);
                            Console.WriteLine($"Created container {container.Value.Name}");
                        }
                    }
                }

                return blobContainerClient;
            }
            catch
            {
                Console.WriteLine($"Unable to parse stroage reference or connect to storage account {connectionString}.");
                throw;
            }
        }

        private static void PopulateJobQueue(string resourceType, int unitSize)
        {
            var startId = LastUpdatedToResourceSurrogateId(StartDate);
            var endId = LastUpdatedToResourceSurrogateId(EndDate);
            var resourceTypeId = Source.GetResourceTypeId(resourceType);
            var ranges = Source.GetSurrogateIdRanges(resourceTypeId, startId, endId, unitSize, (int)(2e9 / unitSize));

            var strings = ranges.Select(_ => $"{0};{resourceTypeId};{_.StartId};{_.EndId};{0}").ToList();

            var queueConn = new SqlConnection(Queue.ConnectionString);
            queueConn.Open();
            using var drop = new SqlCommand("IF object_id('##StoreCopyWorkQueue') IS NOT NULL DROP TABLE ##StoreCopyWorkQueue", queueConn) { CommandTimeout = 60 };
            drop.ExecuteNonQuery();
            using var create = new SqlCommand("CREATE TABLE ##StoreCopyWorkQueue (String varchar(255))", queueConn) { CommandTimeout = 60 };
            create.ExecuteNonQuery();

            using var cmd = new SqlCommand("INSERT INTO ##StoreCopyWorkQueue SELECT String FROM @Strings", queueConn) { CommandTimeout = 60 };
            var stringListParam = new SqlParameter { ParameterName = "@Strings" };
            stringListParam.AddStringList(strings);
            cmd.Parameters.Add(stringListParam);
            cmd.ExecuteNonQuery();

            using var insert = new SqlCommand(
                @"
TRUNCATE TABLE dbo.JobQueue

DECLARE @Definitions StringList
INSERT INTO @Definitions
  SELECT ResourceTypeId+';'+MinId+';'+MaxId+';'+ResourceCount
    FROM (SELECT UnitId = max(CASE WHEN ordinal = 1 THEN value END)
                ,ResourceTypeId = max(CASE WHEN ordinal = 2 THEN value END)
                ,MinId = max(CASE WHEN ordinal = 3 THEN value END)
                ,MaxId = max(CASE WHEN ordinal = 4 THEN value END)
                ,ResourceCount = max(CASE WHEN ordinal = 5 THEN value END)
            FROM ##StoreCopyWorkQueue
                 CROSS APPLY string_split(String, ';', 1)
            GROUP BY
                 String
         ) A

EXECUTE dbo.EnqueueJobs @QueueType = 1, @Definitions = @Definitions, @ForceOneActiveJobGroup = 1
                ",
                queueConn)
            { CommandTimeout = 600 };
            using var insertReader = insert.ExecuteReader();
            var ids = new Dictionary<long, long>();
            while (insertReader.Read())
            {
                ids.Add(insertReader.GetInt64(1), insertReader.GetInt64(0));
            }

            Console.WriteLine($"Export.{ResourceType}: enqueued={ids.Count} jobs.");

            queueConn.Close();
        }

        private static long LastUpdatedToResourceSurrogateId(DateTime dateTime)
        {
            long id = dateTime.TruncateToMillisecond().Ticks << 3;

            Debug.Assert(id >= 0, "The ID should not have become negative");
            return id;
        }

        private static DateTime ResourceSurrogateIdToLastUpdated(long resourceSurrogateId)
        {
            var dateTime = new DateTime(resourceSurrogateId >> 3, DateTimeKind.Utc);
            return dateTime.TruncateToMillisecond();
        }

        private static DateTime TruncateToMillisecond(this DateTime dt)
        {
            return dt;
        }
    }
}
