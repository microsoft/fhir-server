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
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Health.Fhir.Store.Utils;

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

        public static void Main()
        {
            Console.WriteLine($"Source=[{Source.ShowConnectionString()}]");
            Console.WriteLine($"Queue=[{Queue.ShowConnectionString()}]");
            if (RebuildWorkQueue)
            {
                PopulateJobQueue(ResourceType, UnitSize);
            }

            Export();
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
            Console.WriteLine($"Export.{ResourceType}.threads={Threads}: {(stop ? "FAILED" : "completed")} at {DateTime.Now:s}, resources={_resourcesTotal} speed={_resourcesTotal / _sw.Elapsed.TotalSeconds:N0} resources/sec elapsed={_sw.Elapsed.TotalSeconds:N0} sec.");
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
                    Queue.DequeueJob(null, out var _, out unitId, out version, out resourceTypeId, out minId, out maxId);
                    if (resourceTypeId.HasValue)
                    {
                        var container = GetContainer(BlobConnectionString, BlobContainerName);
                        var resources = Export(resourceTypeId.Value, container, long.Parse(minId), long.Parse(maxId));
                        Interlocked.Add(ref _resourcesTotal, resources);
                        Queue.CompleteJob(unitId, false, version, resources);
                    }

                    if (_swReport.Elapsed.TotalSeconds > ReportingPeriodSec)
                    {
                        lock (_swReport)
                        {
                            if (_swReport.Elapsed.TotalSeconds > ReportingPeriodSec)
                            {
                                Console.WriteLine($"Export.{ResourceType}.threads={Threads}.Writes={WritesEnabled}.Decompress={DecompressEnabled}.Reads={ReadsEnabled}: Resources={_resourcesTotal} secs={(int)_sw.Elapsed.TotalSeconds} speed={(int)(_resourcesTotal / _sw.Elapsed.TotalSeconds)} resources/sec");
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

            var resources = Source.GetData(resourceTypeId, minId, maxId).ToList(); // ToList will fource reading from SQL even when writes are disabled
            var strings = new List<string>();
            if (DecompressEnabled)
            {
                foreach (var res in resources)
                {
                    using var mem = new MemoryStream(res);
                    strings.Add(CompressedRawResourceConverterCopy.ReadCompressedRawResource(mem));
                }
            }

            if (WritesEnabled)
            {
                WriteBatchOfLines(container, strings, $"{ResourceType}-{minId}-{maxId}.ndjson");
            }

            return resources.Count;
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
            using var sourceConn = new SqlConnection(Source.ConnectionString);
            sourceConn.Open();
            using var select = new SqlCommand(
                @"
SELECT convert(varchar,UnitId)
       +';'+convert(varchar,(SELECT ResourceTypeId FROM dbo.ResourceType B WHERE B.Name = @ResourceType))
       +';'+convert(varchar,min(ResourceSurrogateId))
       +';'+convert(varchar,max(ResourceSurrogateId))
       +';'+convert(varchar,count(*))
  FROM (SELECT UnitId = isnull(convert(int, (row_number() OVER (ORDER BY ResourceSurrogateId) - 1) / @UnitSize), 0)
              ,ResourceSurrogateId
          FROM dbo.Resource
          WHERE ResourceTypeId = (SELECT ResourceTypeId FROM dbo.ResourceType B WHERE B.Name = @ResourceType)
            AND IsHistory = 0
            AND ResourceSurrogateId BETWEEN datediff_big(millisecond,'0001-01-01',@StartDate) * 80000 AND datediff_big(millisecond,'0001-01-01',@EndDate) * 80000 + (80000 - 1)
       ) A
  GROUP BY
       UnitId
  ORDER BY
       UnitId
  OPTION (MAXDOP 8) -- 0 7:17 -- 1 29:14 -- 8 6:43
                 ",
                sourceConn)
            { CommandTimeout = 3600 };
            select.Parameters.AddWithValue("@UnitSize", unitSize);
            select.Parameters.AddWithValue("@ResourceType", resourceType);
            select.Parameters.AddWithValue("@StartDate", StartDate);
            select.Parameters.AddWithValue("@EndDate", EndDate);
            using var selectReader = select.ExecuteReader();
            var strings = new List<string>();
            while (selectReader.Read())
            {
                strings.Add(selectReader.GetString(0));
            }

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
    }
}
