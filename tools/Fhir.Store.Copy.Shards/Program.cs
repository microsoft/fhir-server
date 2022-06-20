// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.SqlServer.Database;
using Microsoft.Health.Fhir.Store.SqlUtils;
using Microsoft.Health.Fhir.Store.Utils;

namespace Microsoft.Health.Fhir.Store.Copy
{
    public static class Program
    {
        private static readonly string SourceConnectionString = ConfigurationManager.ConnectionStrings["SourceDatabase"].ConnectionString;
        private static readonly string TargetConnectionString = ConfigurationManager.ConnectionStrings["TargetDatabase"].ConnectionString;
        private static readonly string QueueConnectionString = ConfigurationManager.ConnectionStrings["QueueDatabase"].ConnectionString;
        private static readonly int Threads = int.Parse(ConfigurationManager.AppSettings["Threads"]);
        private static readonly int UnitSize = int.Parse(ConfigurationManager.AppSettings["UnitSize"]);
        private static readonly int MaxRetries = int.Parse(ConfigurationManager.AppSettings["MaxRetries"]);
        private static readonly bool QueueOnly = bool.Parse(ConfigurationManager.AppSettings["QueueOnly"]);
        private static readonly bool WritesEnabled = bool.Parse(ConfigurationManager.AppSettings["WritesEnabled"]);
        private static bool stop = false;
        private static readonly SqlService Target = new SqlService(TargetConnectionString);
        private static readonly SqlService Source = new SqlService(SourceConnectionString);
        private static readonly SqlService Queue = new SqlService(QueueConnectionString);

        public static void Main(string[] args)
        {
            Console.WriteLine($"Source=[{Source.ShowConnectionString()}]");
            Console.WriteLine($"Target=[{Target.ShowConnectionString()}]");
            Console.WriteLine($"Queue=[{Queue.ShowConnectionString()}]");
            var method = args.Length > 0 ? args[0] : "merge";
            if (method == "setupdb")
            {
                SetupDb.Publish(TargetConnectionString, "Microsoft.Health.Fhir.SqlServer.Database.dacpac");
                SetupDb.Publish(TargetConnectionString, "Fhir.Store.Copy.Database.dacpac");
                SetupDb.Publish(QueueConnectionString, "Microsoft.Health.Fhir.SqlServer.Database.dacpac");
                SetupDb.Publish(QueueConnectionString, "Fhir.Store.Copy.Database.dacpac");
            }
            else
            {
                Target.RegisterDatabaseLogging();
                PopulateStoreCopyWorkQueue(UnitSize);

                Copy(method);
            }
        }

        public static void Copy(string method)
        {
            var tasks = new List<Task>();
            for (var i = 0; i < Threads; i++)
            {
                var thread = i;
                tasks.Add(BatchExtensions.StartTask(() =>
                {
                    Copy(thread, method);
                }));
            }

            Task.WaitAll(tasks.ToArray());
        }

        private static void Copy(int thread, string method)
        {
            Console.WriteLine($"Copy.{thread}: started at {DateTime.UtcNow:s}");
            var sw = Stopwatch.StartNew();
            var resourceTypeId = (short?)0;
            while (resourceTypeId.HasValue && !stop)
            {
                string minIdOrUrl = null;
                var partitionId = (byte)0;
                var unitId = 0;
                var retries = 0;
                var maxRetries = MaxRetries;
                var resourceCount = 0;
                var useSecondaryStore = thread % 2 == 1;
retry:
                try
                {
                    Queue.DequeueStoreCopyWorkQueue(PartitionByPartOfResourceId ? thread : null, out resourceTypeId, out partitionId, out unitId, out minIdOrUrl, out var maxId);
                    if (!QueueOnly && resourceTypeId.HasValue)
                    {
                        if (method == "bcp")
                        {
                            CopyViaBcp(thread, resourceTypeId.Value, partitionId, unitId, long.Parse(minIdOrUrl), long.Parse(maxId));
                        }
                        else
                        {
                            if (PartitionByPartOfResourceId)
                            {
                                var sourceContainer = GetContainer(SourceBlobConnectionString, SourceBlobContainerName);
                                resourceCount = CopyViaSqlPartitionByPartOfResourceId(method == "merge", resourceTypeId.Value, partitionId, unitId, sourceContainer, minIdOrUrl, useSecondaryStore);
                            }
                            else
                            {
                                CopyViaSql(method == "merge", thread, resourceTypeId.Value, unitId, minIdOrUrl, maxId, SortBySurrogateId);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Copy.{method}.{thread}.{resourceTypeId}.{minIdOrUrl}: error={e}");
                    Target.LogEvent($"Copy.{method}", "Error", $"{thread}.{resourceTypeId}.{minIdOrUrl}", text: e.ToString());
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
                        Target.CompleteStoreCopyWorkUnit(partitionId, unitId, true);
                    }

                    throw;
                }

                Queue.CompleteStoreCopyWorkUnit(partitionId, unitId, false, PartitionByPartOfResourceId ? resourceCount : null);
            }

            Console.WriteLine($"Copy.{method}.{thread}: {(stop ? "FAILED" : "completed")} at {DateTime.Now:s}, elapsed={sw.Elapsed.TotalSeconds:N0} sec.");
        }

        private static void CopyViaSql(bool isMerge, int thread, short resourceTypeId, int unitId, string minId, string maxId, bool convertToLong)
        {
            var sw = Stopwatch.StartNew();
            var surrIdMap = new Dictionary<long, int>();
            var count = 0;
            List<Resource> resources;
            var st = DateTime.UtcNow;
            var resourcesOrig = Source.GetData(_ => new Resource(_), resourceTypeId, minId, maxId, convertToLong).ToList();
            if (isMerge) // redefine surr id
            {
                var resourcesDedup = new List<Resource>();

                foreach (var resource in resourcesOrig)
                {
                    if (!surrIdMap.ContainsKey(resource.ResourceSurrogateId))
                    {
                        count++;
                        surrIdMap.Add(resource.ResourceSurrogateId, count);
                        resourcesDedup.Add(resource);
                    }
                }

                resources = resourcesDedup.Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
            }
            else
            {
                resources = resourcesOrig.ToList();
            }

            var referenceSearchParams = Source.GetData(_ => new ReferenceSearchParam(_), resourceTypeId, minId, maxId, convertToLong).ToList();
            if (referenceSearchParams.Count == 0)
            {
                referenceSearchParams = null;
            }
            else if (isMerge)
            {
                referenceSearchParams = referenceSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
            }

            var tokenSearchParams = Source.GetData(_ => new TokenSearchParam(_), resourceTypeId, minId, maxId, convertToLong).ToList();
            if (tokenSearchParams.Count == 0)
            {
                tokenSearchParams = null;
            }
            else if (isMerge)
            {
                tokenSearchParams = tokenSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
            }

            var compartmentAssignments = Source.GetData(_ => new CompartmentAssignment(_), resourceTypeId, minId, maxId, convertToLong).ToList();
            if (compartmentAssignments.Count == 0)
            {
                compartmentAssignments = null;
            }
            else if (isMerge)
            {
                compartmentAssignments = compartmentAssignments.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
            }

            var tokenTexts = Source.GetData(_ => new TokenText(_), resourceTypeId, minId, maxId, convertToLong).ToList();
            if (tokenTexts.Count == 0)
            {
                tokenTexts = null;
            }
            else if (isMerge)
            {
                tokenTexts = tokenTexts.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
            }

            var dateTimeSearchParams = Source.GetData(_ => new DateTimeSearchParam(_), resourceTypeId, minId, maxId, convertToLong).ToList();
            if (dateTimeSearchParams.Count == 0)
            {
                dateTimeSearchParams = null;
            }
            else if (isMerge)
            {
                dateTimeSearchParams = dateTimeSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
            }

            var tokenQuantityCompositeSearchParams = Source.GetData(_ => new TokenQuantityCompositeSearchParam(_), resourceTypeId, minId, maxId, convertToLong).ToList();
            if (tokenQuantityCompositeSearchParams.Count == 0)
            {
                tokenQuantityCompositeSearchParams = null;
            }
            else if (isMerge)
            {
                tokenQuantityCompositeSearchParams = tokenQuantityCompositeSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
            }

            var quantitySearchParams = Source.GetData(_ => new QuantitySearchParam(_), resourceTypeId, minId, maxId, convertToLong).ToList();
            if (quantitySearchParams.Count == 0)
            {
                quantitySearchParams = null;
            }
            else if (isMerge)
            {
                quantitySearchParams = quantitySearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
            }

            var stringSearchParams = Source.GetData(_ => new StringSearchParam(_), resourceTypeId, minId, maxId, convertToLong).ToList();
            if (stringSearchParams.Count == 0)
            {
                stringSearchParams = null;
            }
            else if (isMerge)
            {
                stringSearchParams = stringSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
            }

            var tokenTokenCompositeSearchParams = Source.GetData(_ => new TokenTokenCompositeSearchParam(_), resourceTypeId, minId, maxId, convertToLong).ToList();
            if (tokenTokenCompositeSearchParams.Count == 0)
            {
                tokenTokenCompositeSearchParams = null;
            }
            else if (isMerge)
            {
                tokenTokenCompositeSearchParams = tokenTokenCompositeSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
            }

            var tokenStringCompositeSearchParams = Source.GetData(_ => new TokenStringCompositeSearchParam(_), resourceTypeId, minId, maxId, convertToLong).ToList();
            if (tokenStringCompositeSearchParams.Count == 0)
            {
                tokenStringCompositeSearchParams = null;
            }
            else if (isMerge)
            {
                tokenStringCompositeSearchParams = tokenStringCompositeSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
            }

            if (WritesEnabled)
            {
                Target.InsertResources(isMerge, resources, referenceSearchParams, tokenSearchParams, compartmentAssignments, tokenTexts, dateTimeSearchParams, tokenQuantityCompositeSearchParams, quantitySearchParams, stringSearchParams, tokenTokenCompositeSearchParams, tokenStringCompositeSearchParams, SingleTransation);
            }

            Console.WriteLine($"Copy.{(isMerge ? "merge" : "insert")}.sort={(SortBySurrogateId ? "SurrogateId" : "ResourceId")}.{thread}.{unitId}.{resourceTypeId}.{minId}: completed at {DateTime.Now:s}, elapsed={sw.Elapsed.TotalSeconds:N0} sec.");
        }

        private static void RunOsCommand(string filename, string arguments, bool redirectOutput)
        {
            var processStartInfo = new ProcessStartInfo(filename, arguments)
            {
                UseShellExecute = !redirectOutput,
                RedirectStandardError = redirectOutput, // if redirected then parallel perf drops
                RedirectStandardOutput = redirectOutput,
                CreateNoWindow = false, // make everything visible and killable
            };

            using var process = Process.Start(processStartInfo);
            if (process == null)
            {
                throw new ArgumentException("Process start information wasn't successfully created.");
            }

            if (redirectOutput)
            {
                process.OutputDataReceived += (sender, args) => Console.WriteLine(args.Data);
                process.BeginOutputReadLine();
                process.ErrorDataReceived += (sender, args) => Console.WriteLine(args.Data);
                process.BeginErrorReadLine();
            }

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new ArgumentException($"Error {process.ExitCode} running {filename}.");
            }
        }

        private static void PopulateStoreCopyWorkQueue(int unitSize)
        {
            // if target is populated don't do anything
            if (Queue.StoreCopyWorkQueueIsNotEmpty())
            {
                return;
            }

            var sourceConn = new SqlConnection(Source.ConnectionString);
            sourceConn.Open();
            var sql = SortBySurrogateId ?
                                @"
SELECT PartUnitId = isnull(convert(int, (row_number() OVER (PARTITION BY ResourceTypeId ORDER BY ResourceSurrogateId) - 1) / @UnitSize), 0)
      ,ResourceTypeId
      ,ResourceSurrogateId
  INTO #tmp
  FROM dbo.Resource

SELECT PartUnitId
      ,ResourceTypeId
      ,MinId = min(ResourceSurrogateId)
      ,MaxId = max(ResourceSurrogateId)
      ,ResourceCount = count(*)
  INTO #tmp2
  FROM #tmp
  GROUP BY
       ResourceTypeId
      ,PartUnitId

SELECT UnitId = convert(int, row_number() OVER (ORDER BY RandId))
      ,ResourceTypeId
      ,MinId
      ,MaxId
      ,ResourceCount
  INTO ##StoreCopyWorkQueue
  FROM (SELECT RandId = newid()
              ,ResourceTypeId
              ,MinId
              ,MaxId
              ,ResourceCount
          FROM #tmp2
       ) A                     
                                "
                                :
                                @"
SELECT UnitId = convert(int, row_number() OVER (ORDER BY RandId))
      ,ResourceTypeId
      ,MinId
      ,MaxId
      ,ResourceCount
  INTO ##StoreCopyWorkQueue
  FROM (SELECT RandId = newid()
              ,PartUnitId
              ,ResourceTypeId
              ,MinId = min(ResourceId)
              ,MaxId = max(ResourceId)
              ,ResourceCount = count(*)
          FROM (SELECT PartUnitId = isnull(convert(int, (row_number() OVER (PARTITION BY ResourceTypeId ORDER BY ResourceId) - 1) / @UnitSize), 0)
                      ,ResourceTypeId
                      ,ResourceId
                  FROM dbo.Resource
               ) A
          GROUP BY
               ResourceTypeId
              ,PartUnitId
       ) A
                                ";
            using var sourceCommand = new SqlCommand(sql, sourceConn) { CommandTimeout = 7200 }; // this takes 30 minutes on db with 2B resources
            sourceCommand.Parameters.AddWithValue("@UnitSize", unitSize);
            sourceCommand.ExecuteNonQuery();

            var param = $@"/C bcp.exe ##StoreCopyWorkQueue out {Path}\StoreCopyWorkQueue.dat /c {BcpSourceConnStr}";
            RunOsCommand("cmd.exe ", param, true);
            Queue.LogEvent("BcpOut", "End", "StoreCopyWorkQueue", text: param);

            sourceConn.Close(); // close connection after bcp

            var queueConn = new SqlConnection(Queue.ConnectionString);
            queueConn.Open();
            using var command = new SqlCommand(
                @"
SELECT UnitId,ResourceTypeId,MinIdOrUrl,MaxId,ResourceCount INTO ##StoreCopyWorkQueue FROM dbo.StoreCopyWorkQueue WHERE 1 = 2",
                queueConn)
            { CommandTimeout = 60 };
            command.ExecuteNonQuery();

            param = $@"/C bcp.exe ##StoreCopyWorkQueue in {Path}\StoreCopyWorkQueue.dat /c {BcpQueueConnStr}";
            RunOsCommand("cmd.exe ", param, true);
            Queue.LogEvent("BcpIn", "End", "StoreCopyWorkQueue", text: param);

            using var insert = new SqlCommand(
                @"
INSERT INTO dbo.StoreCopyWorkQueue 
        (PartitionId,UnitId,ResourceTypeId,MinIdOrUrl,MaxId,ResourceCount) 
  SELECT UnitId % 16,UnitId,ResourceTypeId,MinIdOrUrl,MaxId,ResourceCount 
    FROM ##StoreCopyWorkQueue",
                queueConn)
            { CommandTimeout = 600 };
            insert.ExecuteNonQuery();

            queueConn.Close();
        }
    }
}
