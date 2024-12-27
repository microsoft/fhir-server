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
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Files.DataLake;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Store.Utils;
using Microsoft.Health.Internal.Fhir.Exporter;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Store.Export
{
    public static class Program
    {
        private static readonly string ConnectionString = ConfigurationManager.ConnectionStrings["Database"].ConnectionString;
        private static readonly string AdlsUri = ConfigurationManager.AppSettings["AdlsUri"];
        private static readonly string AdlsUAMI = ConfigurationManager.AppSettings["AdlsUAMI"];
        private static readonly string AdlsContainerName = ConfigurationManager.AppSettings["AdlsContainerName"];
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
        private static readonly SqlService Source = new SqlService(ConnectionString);
        private static bool stop = false;
        private static long _resourcesTotal = 0L;
        private static Stopwatch _swReport = Stopwatch.StartNew();
        private static Stopwatch _sw = Stopwatch.StartNew();

        private static Stopwatch _database = new Stopwatch();
        private static Stopwatch _unzip = new Stopwatch();
        private static Stopwatch _blob = new Stopwatch();
        private static BlobContainerClient _blobContainer;
        private static DataLakeFileSystemClient _fileSystem;

        public static void Main(string[] args)
        {
            if (args.Length > 0 && (args[0] == "random" || args[0] == "sorted"))
            {
                var count = args.Length > 1 ? int.Parse(args[1]) : 100;
                _blobContainer = GetContainer(AdlsUri, AdlsUAMI, AdlsContainerName);
                _fileSystem = new DataLakeFileSystemClient(new Uri($"{AdlsUri}/{AdlsContainerName}"), string.IsNullOrEmpty(AdlsUAMI) ? new InteractiveBrowserCredential() : new ManagedIdentityCredential(AdlsUAMI));
                try
                {
                    _fileSystem.GetFileClient("blobName").OpenRead();
                }
                catch
                {
                }

                var parall = args.Length > 2 ? int.Parse(args[2]) : 8;

                if (args[0] == "random")
                {
                    RandomReads(count, parall);
                }
                else
                {
                    SortedReads(count, parall);
                }
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
                    if (RebuildWorkQueue)
                    {
                        PopulateJobQueue(ResourceType, UnitSize);
                    }

                    Export();
                }
            }
        }

        public static void RandomReads(int count, int parall)
        {
            var maxId = LastUpdatedToResourceSurrogateId(DateTime.UtcNow);
            var ranges = Source.GetSurrogateIdRanges(96, 0, maxId, 10000, 10000);
            var refs = new List<(long FileId, int OffsetInFile)>();
            foreach (var range in ranges)
            {
                refs.AddRange(Source.GetRefs(96, range.StartId, range.EndId));
            }

            Console.WriteLine($"RandomRead: file/offsets = {refs.Count}");

            var blobDurations = new List<double>();
            var fileDurations = new List<double>();
            for (var l = 0; l < 10; l++)
            {
                var subSetRefs = refs.OrderBy(_ => RandomNumberGenerator.GetInt32(100000000)).Take(count).ToList();
                var sw = Stopwatch.StartNew();
                var resources = GetRawResourceFromAdls(subSetRefs, true, parall);
                blobDurations.Add(sw.Elapsed.TotalMilliseconds);
                Console.WriteLine($"BLOB.RandomRead.{resources.Count}.parall={parall}: total={sw.Elapsed.TotalMilliseconds} msec perLine={sw.Elapsed.TotalMilliseconds / resources.Count} msec");
                subSetRefs = refs.OrderBy(_ => RandomNumberGenerator.GetInt32(100000000)).Take(count).ToList();
                sw = Stopwatch.StartNew();
                resources = GetRawResourceFromAdls(subSetRefs, false, parall);
                fileDurations.Add(sw.Elapsed.TotalMilliseconds);
                Console.WriteLine($"File.RandomRead.{resources.Count}.parall={parall}: total={sw.Elapsed.TotalMilliseconds} msec perLine={sw.Elapsed.TotalMilliseconds / resources.Count} msec");
            }

            Console.WriteLine($"BLOB.RandomRead.parall={parall}: total={blobDurations.Sum() / 10} msec");
            Console.WriteLine($"File.RandomRead.parall={parall}: total={fileDurations.Sum() / 10} msec");
        }

        public static void SortedReads(int count, int parall)
        {
            var maxId = LastUpdatedToResourceSurrogateId(DateTime.UtcNow);
            var ranges = Source.GetSurrogateIdRanges(96, 0, maxId, 10000, 10000);
            var refs = new List<(long FileId, int OffsetInFile)>();
            foreach (var range in ranges)
            {
                refs.AddRange(Source.GetRefs(96, range.StartId, range.EndId));
            }

            Console.WriteLine($"SortedRead: file/offsets = {refs.Count}");

            var blobDurations = new List<double>();
            var fileDurations = new List<double>();
            var blobResources = 0L;
            var fileResources = 0L;
            var loop = 0;
            foreach (var r in refs.GroupBy(_ => _.FileId))
            {
                var subSetRefs = r.ToList();

                var sw = Stopwatch.StartNew();
                var resources = GetRawResourceFromAdls(subSetRefs, true, parall);
                Console.WriteLine($"Ignore BLOB.SortedRead.{resources.Count}.parall={parall}: total={sw.Elapsed.TotalMilliseconds} msec perLine={sw.Elapsed.TotalMilliseconds / resources.Count} msec");

                sw = Stopwatch.StartNew();
                resources = GetRawResourceFromAdls(subSetRefs, false, parall);
                fileDurations.Add(sw.Elapsed.TotalMilliseconds);
                fileResources += resources.Sum(_ => _.Length);
                Console.WriteLine($"File.SortedRead.{resources.Count}.parall={parall}: total={sw.Elapsed.TotalMilliseconds} msec perLine={sw.Elapsed.TotalMilliseconds / resources.Count} msec");

                sw = Stopwatch.StartNew();
                resources = GetRawResourceFromAdls(subSetRefs, true, parall);
                blobDurations.Add(sw.Elapsed.TotalMilliseconds);
                blobResources += resources.Sum(_ => _.Length);
                Console.WriteLine($"BLOB.SortedRead.{resources.Count}.parall={parall}: total={sw.Elapsed.TotalMilliseconds} msec perLine={sw.Elapsed.TotalMilliseconds / resources.Count} msec");

                loop++;
                if (loop >= 10)
                {
                    break;
                }
            }

            Console.WriteLine($"BLOB.SortedRead.parall={parall}: resources={blobResources} total={blobDurations.Sum()} msec");
            Console.WriteLine($"File.SortedRead.parall={parall}: resources={fileResources} total={fileDurations.Sum()} msec");
        }

        public static IReadOnlyList<string> GetRawResourceFromAdls(IReadOnlyList<(long FileId, int OffsetInFile)> resourceRefs, bool isBlob, int parall)
        {
            var start = DateTime.UtcNow;
            var results = new List<string>();
            if (resourceRefs == null || resourceRefs.Count == 0)
            {
                return results;
            }

            if (isBlob)
            {
                var resourceRefsByTransaction = resourceRefs.GroupBy(_ => _.FileId);
                Parallel.ForEach(resourceRefsByTransaction, new ParallelOptions { MaxDegreeOfParallelism = parall }, (group) =>
                {
                    var transactionId = group.Key;
                    var blobName = GetBlobName(transactionId);
                    var blobClient = _blobContainer.GetBlobClient(blobName);
                    using var stream = blobClient.OpenRead();
                    using var reader = new StreamReader(stream);
                    foreach (var resourceRef in group)
                    {
                        reader.DiscardBufferedData();
                        stream.Position = resourceRef.OffsetInFile;
                        var line = reader.ReadLine();
                        lock (results)
                        {
                            results.Add(line);
                        }
                    }
                });
            }
            else
            {
                var resourceRefsByTransaction = resourceRefs.GroupBy(_ => _.FileId);
                Parallel.ForEach(resourceRefsByTransaction, new ParallelOptions { MaxDegreeOfParallelism = parall }, (group) =>
                {
                    var transactionId = group.Key;
                    var blobName = GetBlobName(transactionId);
                    var fileClient = _fileSystem.GetFileClient(blobName);
                    using var stream = fileClient.OpenRead();
                    using var reader = new StreamReader(stream);
                    foreach (var resourceRef in group)
                    {
                        reader.DiscardBufferedData();
                        stream.Position = resourceRef.OffsetInFile;
                        var line = reader.ReadLine();
                        lock (results)
                        {
                            results.Add(line);
                        }
                    }
                });
            }

            Source.LogEvent("GetRawResourceFromAdls", "Warn", $"Resources={results.Count}", start);

            return results;
        }

        internal static string GetBlobName(long fileId)
        {
            return $"hash-{GetPermanentHashCode(fileId)}/transaction-{fileId}.ndjson";
        }

        private static string GetPermanentHashCode(long tr)
        {
            var hashCode = 0;
            foreach (var c in tr.ToString()) // Don't convert to LINQ. This is 10% faster.
            {
                hashCode = unchecked((hashCode * 251) + c);
            }

            return (Math.Abs(hashCode) % 512).ToString().PadLeft(3, '0');
        }

        public static void WriteAndReadAdls(int count, int bufferKB)
        {
            GetContainer(AdlsUri, AdlsUAMI, "fhir-hs-new-one-file");

            var fileName = "transaction-353229202.ndjson";

            var swGlobal = Stopwatch.StartNew();

            var fileSystem = new DataLakeFileSystemClient(new Uri(AdlsUri), string.IsNullOrEmpty(AdlsUAMI) ? new InteractiveBrowserCredential() : new ManagedIdentityCredential(AdlsUAMI));
            var fileClient = fileSystem.GetFileClient($"fhir-hs-new-one-file/{fileName}");

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
            fileClient = fileSystem.GetFileClient($"testadls/{fileName}");
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

            var container = GetContainer(AdlsUri, AdlsUAMI, "testblob");

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
            container = GetContainer(AdlsUri, AdlsUAMI, "testblob");
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
            Console.WriteLine($"ExportNoSource.{ResourceType}: ranges={ranges.Count}.");
            var container = GetContainer(AdlsUri, AdlsUAMI, AdlsContainerName);
            foreach (var range in ranges)
            {
                Export(resourceTypeId, container, range.StartId, range.EndId);
                if (_swReport.Elapsed.TotalSeconds > ReportingPeriodSec)
                {
                    lock (_swReport)
                    {
                        if (_swReport.Elapsed.TotalSeconds > ReportingPeriodSec)
                        {
                            Console.WriteLine($"ExportNoSource.{ResourceType}.threads={Threads}.Writes={WritesEnabled}.Decompress={DecompressEnabled}.Reads={ReadsEnabled}: Resources={_resourcesTotal} secs={(int)_sw.Elapsed.TotalSeconds} speed={(int)(_resourcesTotal / _sw.Elapsed.TotalSeconds)} resources/sec DB={_database.Elapsed.TotalSeconds} sec UnZip={_unzip.Elapsed.TotalSeconds} sec Blob={_blob.Elapsed.TotalSeconds}");
                            _swReport.Restart();
                        }
                    }
                }
            }

            Console.WriteLine($"ExportNoSource.{ResourceType}.threads={Threads}: {(stop ? "FAILED" : "completed")} at {DateTime.Now:s}, resources={_resourcesTotal} speed={_resourcesTotal / _sw.Elapsed.TotalSeconds:N0} resources/sec elapsed={_sw.Elapsed.TotalSeconds:N0} sec DB={_database.Elapsed.TotalSeconds} sec UnZip={_unzip.Elapsed.TotalSeconds} sec Blob={_blob.Elapsed.TotalSeconds}");
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
                    Source.DequeueJob(out var _, out unitId, out version, out resourceTypeId, out minId, out maxId);
                    if (resourceTypeId.HasValue)
                    {
                        var container = GetContainer(AdlsUri, AdlsUAMI, AdlsContainerName);
                        var resources = Export(resourceTypeId.Value, container, long.Parse(minId), long.Parse(maxId));
                        Source.CompleteJob(unitId, false, version, resources);
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
                    Source.LogEvent($"Export", "Error", $"{ResourceType}.{thread}.{minId}.{maxId}: error={e}", DateTime.Now);
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
                        Source.CompleteJob(unitId, true, version);
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

        private static BlobContainerClient GetContainer(string adlsUri, string adlsUAMI, string adlsContainerName)
        {
            try
            {
                if (string.IsNullOrEmpty(adlsUri))
                {
                    throw new ArgumentNullException(nameof(adlsUri));
                }

                var blobServiceClient = new BlobServiceClient(new Uri(adlsUri), string.IsNullOrEmpty(adlsUAMI) ? new InteractiveBrowserCredential() : new ManagedIdentityCredential(adlsUAMI));
                var blobContainerClient = blobServiceClient.GetBlobContainerClient(adlsContainerName);

                if (!blobContainerClient.Exists())
                {
                    var container = blobServiceClient.CreateBlobContainer(adlsContainerName);
                    Console.WriteLine($"Created container {container.Value.Name}");
                }

                return blobContainerClient;
            }
            catch
            {
                Console.WriteLine($"Unable to parse stroage reference or connect to storage account {adlsUri}.");
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

            var queueConn = new SqlConnection(Source.ConnectionString);
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
