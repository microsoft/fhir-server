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
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.SqlServer.Database;
using Microsoft.Health.Fhir.Store.Utils;

namespace Microsoft.Health.Fhir.Store.Copy
{
    public static class Program
    {
        private static readonly string SourceConnectionString = ConfigurationManager.ConnectionStrings["SourceDatabase"].ConnectionString;
        private static readonly string TargetConnectionString = ConfigurationManager.ConnectionStrings["TargetDatabase"].ConnectionString;
        private static readonly string Path = ConfigurationManager.AppSettings["Path"];
        private static readonly string Tables = ConfigurationManager.AppSettings["Tables"];
        private static readonly int Threads = int.Parse(ConfigurationManager.AppSettings["Threads"]);
        private static readonly int UnitSize = int.Parse(ConfigurationManager.AppSettings["UnitSize"]);
        private static readonly bool SingleTransation = bool.Parse(ConfigurationManager.AppSettings["SingleTransaction"]);
        private static readonly bool SortBySurrogateId = bool.Parse(ConfigurationManager.AppSettings["SortBySurrogateId"]);
        private static bool stop = false;
        private static readonly SqlService Target = new SqlService(TargetConnectionString);
        private static readonly SqlService Source = new SqlService(SourceConnectionString);
        private static readonly string BcpSourceConnStr = Source.GetBcpConnectionString();
        private static readonly string BcpTargetConnStr = Target.GetBcpConnectionString();

        public static void Main(string[] args)
        {
            Console.WriteLine($"Source=[{Source.ShowConnectionString()}]");
            Console.WriteLine($"Target=[{Target.ShowConnectionString()}]");
            var method = args.Length > 0 ? args[0] : "merge";
            if (method == "publish")
            {
                SetupDb.Publish(TargetConnectionString, "Microsoft.Health.Fhir.SqlServer.Database.dacpac");
                SetupDb.Publish(TargetConnectionString, "Fhir.Store.Copy.Database.dacpac");
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
            for (var i = (byte)0; i < Threads; i++)
            {
                var thread = i;
                tasks.Add(BatchExtensions.StartTask(() =>
                {
                    Copy(thread, method);
                }));
            }

            Task.WaitAll(tasks.ToArray());
        }

        private static void Copy(byte thread, string method)
        {
            Console.WriteLine($"CopyFhir.{thread}: started at {DateTime.UtcNow:s}");
            var sw = Stopwatch.StartNew();
            var resourceTypeId = (short?)0;
            while (resourceTypeId.HasValue && !stop)
            {
                Target.DequeueStoreCopyWorkQueue(thread, out resourceTypeId, out var unitId, out var minId, out var maxId);
                if (resourceTypeId.HasValue)
                {
                    if (method == "bcp")
                    {
                        CopyViaBcp(thread, resourceTypeId.Value, unitId, long.Parse(minId), long.Parse(maxId));
                    }
                    else if (method == "insert")
                    {
                        CopyViaInsert(false, thread, resourceTypeId.Value, unitId, long.Parse(minId), long.Parse(maxId));
                    }
                    else
                    {
                        if (SortBySurrogateId)
                        {
                            CopyViaInsert(true, thread, resourceTypeId.Value, unitId, long.Parse(minId), long.Parse(maxId));
                        }
                        else
                        {
                            CopyViaInsert(true, thread, resourceTypeId.Value, unitId, minId, maxId);
                        }
                    }
                }
            }

            Console.WriteLine($"CopyFhir.{thread}: {(stop ? "FAILED" : "completed")} at {DateTime.Now:s}, elapsed={sw.Elapsed.TotalSeconds:N0} sec.");
        }

        private static void CopyViaBcp(byte thread, short resourceTypeId, int unitId, long minSurId, long maxSurId)
        {
            Console.WriteLine($"CopyViaBcp.{thread}.{resourceTypeId}.{minSurId}: started at {DateTime.UtcNow:s}");
            var sw = Stopwatch.StartNew();
            var rand = new Random();
            foreach (var tbl in Tables.Split(',').OrderBy(_ => rand.NextDouble()))
            {
                var mode = $"Tbl={tbl} RT={resourceTypeId} Ids=[{minSurId},{maxSurId}]";
                var retries = 0;
            retryBcp:
                try
                {
                    var correctedMinSurId = Target.GetCorrectedMinResourceSurrogateId(retries, tbl, resourceTypeId, minSurId, maxSurId);
                    var param = $@"/C bcp.exe ""SELECT * FROM dbo.{tbl} WHERE ResourceTypeId = {resourceTypeId} AND ResourceSurrogateId BETWEEN {(correctedMinSurId == minSurId ? minSurId : correctedMinSurId + 1)} AND {maxSurId} ORDER BY ResourceSurrogateId"" queryout {Path}\{tbl}_{thread}.dat /c {BcpSourceConnStr}";
                    var st = DateTime.UtcNow;
                    Target.LogEvent("BcpOut", "Start", mode, text: param);
                    RunOsCommand("cmd.exe ", param, true);
                    Target.LogEvent("BcpOut", "End", mode, startTime: st, text: param);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"CopyViaBcp.{thread}.{resourceTypeId}.{minSurId}.{tbl}: error={e}");
                    Target.LogEvent("BcpOut", "Error", mode, text: e.ToString());
                    retries++;
                    if (retries < 5)
                    {
                        goto retryBcp;
                    }

                    stop = true;
                    Target.CompleteStoreCopyWorkUnit(resourceTypeId, unitId, true);
                    throw;
                }

                try
                {
                    var commitBatchSize = tbl == "Resource" ? (int)1e4 : (int)3e4;
                    var param = $@"/C bcp.exe dbo.{tbl} in {Path}\{tbl}_{thread}.dat /c /q {BcpTargetConnStr} /b{commitBatchSize}";
                    var st = DateTime.UtcNow;
                    Target.LogEvent("BcpIn", "Start", mode, text: param);
                    RunOsCommand("cmd.exe ", param, true);
                    Target.LogEvent("BcpIn", "End", mode, startTime: st, text: param);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"CopyViaBcp.{thread}.{resourceTypeId}.{minSurId}.{tbl}: error={e}");
                    Target.LogEvent("BcpIn", "Error", mode, text: e.ToString());
                    retries++;
                    if (retries < 5)
                    {
                        goto retryBcp;
                    }

                    stop = true;
                    Target.CompleteStoreCopyWorkUnit(resourceTypeId, unitId, true);
                    throw;
                }
            }

            Target.CompleteStoreCopyWorkUnit(resourceTypeId, unitId, false);
            Console.WriteLine($"CopyViaBcp.{thread}.{resourceTypeId}.{minSurId}: completed at {DateTime.Now:s}, elapsed={sw.Elapsed.TotalSeconds:N0} sec.");
        }

        private static void CopyViaInsert(bool isMerge, byte thread, short resourceTypeId, int unitId, long minSurId, long maxSurId)
        {
            Console.WriteLine($"CopyViaInsert.{thread}.{resourceTypeId}.{minSurId}: started at {DateTime.UtcNow:s}");
            var sw = Stopwatch.StartNew();
            var retries = 0;
        retry:
            try
            {
                var surrIdMap = new Dictionary<long, int>();
                var count = 0;
                List<Resource> resources;
                var resourcesOrig = Source.GetData(_ => new Resource(_), resourceTypeId, minSurId, maxSurId);
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

                var referenceSearchParams = Source.GetData(_ => new ReferenceSearchParam(_), resourceTypeId, minSurId, maxSurId).ToList();
                if (referenceSearchParams.Count == 0)
                {
                    referenceSearchParams = null;
                }
                else if (isMerge)
                {
                    referenceSearchParams = referenceSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
                }

                var tokenSearchParams = Source.GetData(_ => new TokenSearchParam(_), resourceTypeId, minSurId, maxSurId).ToList();
                if (tokenSearchParams.Count == 0)
                {
                    tokenSearchParams = null;
                }
                else if (isMerge)
                {
                    tokenSearchParams = tokenSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
                }

                var compartmentAssignments = Source.GetData(_ => new CompartmentAssignment(_), resourceTypeId, minSurId, maxSurId).ToList();
                if (compartmentAssignments.Count == 0)
                {
                    compartmentAssignments = null;
                }
                else if (isMerge)
                {
                    compartmentAssignments = compartmentAssignments.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
                }

                var tokenTexts = Source.GetData(_ => new TokenText(_), resourceTypeId, minSurId, maxSurId).ToList();
                if (tokenTexts.Count == 0)
                {
                    tokenTexts = null;
                }
                else if (isMerge)
                {
                    tokenTexts = tokenTexts.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
                }

                var dateTimeSearchParams = Source.GetData(_ => new DateTimeSearchParam(_), resourceTypeId, minSurId, maxSurId).ToList();
                if (dateTimeSearchParams.Count == 0)
                {
                    dateTimeSearchParams = null;
                }
                else if (isMerge)
                {
                    dateTimeSearchParams = dateTimeSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
                }

                var tokenQuantityCompositeSearchParams = Source.GetData(_ => new TokenQuantityCompositeSearchParam(_), resourceTypeId, minSurId, maxSurId).ToList();
                if (tokenQuantityCompositeSearchParams.Count == 0)
                {
                    tokenQuantityCompositeSearchParams = null;
                }
                else if (isMerge)
                {
                    tokenQuantityCompositeSearchParams = tokenQuantityCompositeSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
                }

                var quantitySearchParams = Source.GetData(_ => new QuantitySearchParam(_), resourceTypeId, minSurId, maxSurId).ToList();
                if (quantitySearchParams.Count == 0)
                {
                    quantitySearchParams = null;
                }
                else if (isMerge)
                {
                    quantitySearchParams = quantitySearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
                }

                var stringSearchParams = Source.GetData(_ => new StringSearchParam(_), resourceTypeId, minSurId, maxSurId).ToList();
                if (stringSearchParams.Count == 0)
                {
                    stringSearchParams = null;
                }
                else if (isMerge)
                {
                    stringSearchParams = stringSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
                }

                var tokenTokenCompositeSearchParams = Source.GetData(_ => new TokenTokenCompositeSearchParam(_), resourceTypeId, minSurId, maxSurId).ToList();
                if (tokenTokenCompositeSearchParams.Count == 0)
                {
                    tokenTokenCompositeSearchParams = null;
                }
                else if (isMerge)
                {
                    tokenTokenCompositeSearchParams = tokenTokenCompositeSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
                }

                var tokenStringCompositeSearchParams = Source.GetData(_ => new TokenStringCompositeSearchParam(_), resourceTypeId, minSurId, maxSurId).ToList();
                if (tokenStringCompositeSearchParams.Count == 0)
                {
                    tokenStringCompositeSearchParams = null;
                }
                else if (isMerge)
                {
                    tokenStringCompositeSearchParams = tokenStringCompositeSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
                }

                Target.InsertResources(isMerge, resources, referenceSearchParams, tokenSearchParams, compartmentAssignments, tokenTexts, dateTimeSearchParams, tokenQuantityCompositeSearchParams, quantitySearchParams, stringSearchParams, tokenTokenCompositeSearchParams, tokenStringCompositeSearchParams, SingleTransation);
            }
            catch (Exception e)
            {
                Console.WriteLine($"CopyViaInsert.{thread}.{resourceTypeId}.{minSurId}: error={e}");
                Target.LogEvent("Merge", "Error", $"{thread}.{resourceTypeId}.{minSurId}", text: e.ToString());
                retries++;
                if (retries < 5)
                {
                    goto retry;
                }

                stop = true;
                Target.CompleteStoreCopyWorkUnit(resourceTypeId, unitId, true);
                throw;
            }

            Target.CompleteStoreCopyWorkUnit(resourceTypeId, unitId, false);
            Console.WriteLine($"CopyViaInsert.{thread}.{resourceTypeId}.{minSurId}: completed at {DateTime.Now:s}, elapsed={sw.Elapsed.TotalSeconds:N0} sec.");
        }

        private static void CopyViaInsert(bool isMerge, byte thread, short resourceTypeId, int unitId, string minId, string maxId)
        {
            Console.WriteLine($"CopyViaInsert.{thread}.{resourceTypeId}.{minId}: started at {DateTime.UtcNow:s}");
            var sw = Stopwatch.StartNew();
            var retries = 0;
        retry:
            try
            {
                var mode = $"RT={resourceTypeId} min={minId} max={maxId}";
                var stTot = DateTime.UtcNow;
                var surrIdMap = new Dictionary<long, int>();
                var count = 0;
                List<Resource> resources;
                var st = DateTime.UtcNow;
                var resourcesOrig = Source.GetData(_ => new Resource(_), resourceTypeId, minId, maxId).ToList();
                Target.LogEvent("GetData", "End", mode, target: "Resource", rows: resourcesOrig.Count, startTime: st);
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

                var referenceSearchParams = Source.GetData(_ => new ReferenceSearchParam(_), resourceTypeId, minId, maxId).ToList();
                if (referenceSearchParams.Count == 0)
                {
                    referenceSearchParams = null;
                }
                else if (isMerge)
                {
                    referenceSearchParams = referenceSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
                }

                var tokenSearchParams = Source.GetData(_ => new TokenSearchParam(_), resourceTypeId, minId, maxId).ToList();
                if (tokenSearchParams.Count == 0)
                {
                    tokenSearchParams = null;
                }
                else if (isMerge)
                {
                    tokenSearchParams = tokenSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
                }

                var compartmentAssignments = Source.GetData(_ => new CompartmentAssignment(_), resourceTypeId, minId, maxId).ToList();
                if (compartmentAssignments.Count == 0)
                {
                    compartmentAssignments = null;
                }
                else if (isMerge)
                {
                    compartmentAssignments = compartmentAssignments.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
                }

                var tokenTexts = Source.GetData(_ => new TokenText(_), resourceTypeId, minId, maxId).ToList();
                if (tokenTexts.Count == 0)
                {
                    tokenTexts = null;
                }
                else if (isMerge)
                {
                    tokenTexts = tokenTexts.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
                }

                var dateTimeSearchParams = Source.GetData(_ => new DateTimeSearchParam(_), resourceTypeId, minId, maxId).ToList();
                if (dateTimeSearchParams.Count == 0)
                {
                    dateTimeSearchParams = null;
                }
                else if (isMerge)
                {
                    dateTimeSearchParams = dateTimeSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
                }

                var tokenQuantityCompositeSearchParams = Source.GetData(_ => new TokenQuantityCompositeSearchParam(_), resourceTypeId, minId, maxId).ToList();
                if (tokenQuantityCompositeSearchParams.Count == 0)
                {
                    tokenQuantityCompositeSearchParams = null;
                }
                else if (isMerge)
                {
                    tokenQuantityCompositeSearchParams = tokenQuantityCompositeSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
                }

                var quantitySearchParams = Source.GetData(_ => new QuantitySearchParam(_), resourceTypeId, minId, maxId).ToList();
                if (quantitySearchParams.Count == 0)
                {
                    quantitySearchParams = null;
                }
                else if (isMerge)
                {
                    quantitySearchParams = quantitySearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
                }

                var stringSearchParams = Source.GetData(_ => new StringSearchParam(_), resourceTypeId, minId, maxId).ToList();
                if (stringSearchParams.Count == 0)
                {
                    stringSearchParams = null;
                }
                else if (isMerge)
                {
                    stringSearchParams = stringSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
                }

                var tokenTokenCompositeSearchParams = Source.GetData(_ => new TokenTokenCompositeSearchParam(_), resourceTypeId, minId, maxId).ToList();
                if (tokenTokenCompositeSearchParams.Count == 0)
                {
                    tokenTokenCompositeSearchParams = null;
                }
                else if (isMerge)
                {
                    tokenTokenCompositeSearchParams = tokenTokenCompositeSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
                }

                var tokenStringCompositeSearchParams = Source.GetData(_ => new TokenStringCompositeSearchParam(_), resourceTypeId, minId, maxId).ToList();
                if (tokenStringCompositeSearchParams.Count == 0)
                {
                    tokenStringCompositeSearchParams = null;
                }
                else if (isMerge)
                {
                    tokenStringCompositeSearchParams = tokenStringCompositeSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.ResourceSurrogateId = surrIdMap[_.ResourceSurrogateId]; return _; }).ToList();
                }

                Target.LogEvent("GetData", "End", mode, startTime: stTot);

                Target.InsertResources(isMerge, resources, referenceSearchParams, tokenSearchParams, compartmentAssignments, tokenTexts, dateTimeSearchParams, tokenQuantityCompositeSearchParams, quantitySearchParams, stringSearchParams, tokenTokenCompositeSearchParams, tokenStringCompositeSearchParams, SingleTransation);
            }
            catch (Exception e)
            {
                Console.WriteLine($"CopyViaInsert.{thread}.{resourceTypeId}.{minId}: error={e}");
                Target.LogEvent("Merge", "Error", $"{thread}.{resourceTypeId}.{minId}", text: e.ToString());
                retries++;
                if (retries < 5)
                {
                    goto retry;
                }

                stop = true;
                Target.CompleteStoreCopyWorkUnit(resourceTypeId, unitId, true);
                throw;
            }

            Target.CompleteStoreCopyWorkUnit(resourceTypeId, unitId, false);
            Console.WriteLine($"CopyViaInsert.{thread}.{resourceTypeId}.{minId}: completed at {DateTime.Now:s}, elapsed={sw.Elapsed.TotalSeconds:N0} sec.");
        }

        private static void RunOsCommand(string filename, string arguments, bool redirectOutput)
        {
            var processStartInfo = new ProcessStartInfo(filename, arguments)
            {
                UseShellExecute = !redirectOutput,
                RedirectStandardError = redirectOutput, // if redirected then parallel perf drops
                RedirectStandardOutput = redirectOutput,
                CreateNoWindow = false // make everything visible and killable
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
            if (Target.StoreCopyWorkQueueIsNotEmpty())
            {
                return;
            }

            var sourceConn = new SqlConnection(Source.ConnectionString);
            sourceConn.Open();
            var sql = SortBySurrogateId ?
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
              ,MinId = min(ResourceSurrogateId)
              ,MaxId = max(ResourceSurrogateId)
              ,ResourceCount = count(*)
          FROM (SELECT PartUnitId = isnull(convert(int, (row_number() OVER (PARTITION BY ResourceTypeId ORDER BY ResourceSurrogateId) - 1) / @UnitSize), 0)
                      ,ResourceTypeId
                      ,ResourceSurrogateId
                  FROM dbo.Resource
               ) A
          GROUP BY
               ResourceTypeId
              ,PartUnitId
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
            Target.LogEvent("BcpOut", "End", "StoreCopyWorkQueue", text: param);

            sourceConn.Close(); // close connection after bcp

            var targetConn = new SqlConnection(Target.ConnectionString);
            targetConn.Open();
            using var command = new SqlCommand(
                @"
SELECT UnitId,ResourceTypeId,MinId,MaxId,ResourceCount INTO ##StoreCopyWorkQueue FROM dbo.StoreCopyWorkQueue WHERE 1 = 2",
                targetConn) { CommandTimeout = 120 };
            command.ExecuteNonQuery();

            param = $@"/C bcp.exe ##StoreCopyWorkQueue in {Path}\StoreCopyWorkQueue.dat /c {BcpTargetConnStr}";
            RunOsCommand("cmd.exe ", param, true);
            Target.LogEvent("BcpIn", "End", "StoreCopyWorkQueue", text: param);

            using var insert = new SqlCommand(
                @"
INSERT INTO dbo.StoreCopyWorkQueue 
        (UnitId,ResourceTypeId,MinId,MaxId,ResourceCount) 
  SELECT UnitId,ResourceTypeId,MinId,MaxId,ResourceCount 
    FROM ##StoreCopyWorkQueue",
                targetConn) { CommandTimeout = 120 };
            insert.ExecuteNonQuery();

            using var update = new SqlCommand(
                @"
UPDATE A
  SET Thread = RowId % @Threads
  FROM (SELECT *, RowId = row_number() OVER (ORDER BY UnitId) - 1 FROM StoreCopyWorkQueue) B
       JOIN StoreCopyWorkQueue A ON A.UnitId = B.UnitId
                 ",
                targetConn) { CommandTimeout = 120 };
            update.Parameters.AddWithValue("@Threads", Threads);
            update.ExecuteNonQuery();

            targetConn.Close();
        }
    }
}
