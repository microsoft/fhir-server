// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Store.Copy;
using Microsoft.Health.Fhir.Store.Utils;

namespace Microsoft.Health.Fhir.Store.WatchDogs
{
    public class CopyWorkerNotSharded
    {
        private int _workers = 1;
        private bool _writesEnabled = false;
        private int _maxRetries = 10;
        private string _sourceConnectionString = string.Empty;

        public CopyWorkerNotSharded(string connectionString)
        {
            Target = new SqlService(connectionString);
            _sourceConnectionString = GetSourceConnectionString();
            if (_sourceConnectionString == null)
            {
                throw new ArgumentException("_sourceConnectionString == null");
            }

            _workers = GetWorkers();
            _writesEnabled = GetWritesEnabled();

            var tasks = new List<Task>();
            var workingTasks = 0L;
            for (var i = 0; i < _workers; i++)
            {
                var worker = i;
                tasks.Add(BatchExtensions.StartTask(() =>
                {
                    Interlocked.Increment(ref workingTasks);
                    Copy(worker);
                    Interlocked.Decrement(ref workingTasks);
                }));
            }

            Thread.Sleep(2000); //// Try to wait till increments happen
            Target.LogEvent($"Copy", "Warn", string.Empty, text: $"workingTasks={Interlocked.Read(ref workingTasks)}");
        }

        public SqlService Target { get; private set; }

        private void Copy(int worker)
        {
            while (true)
            {
                var retries = 0;
                var maxRetries = _maxRetries;
                var version = 0L;
                var jobId = 0L;
            retry:
                try
                {
                    Target.DequeueJob(out _, out jobId, out version, out var definition);
                    if (jobId != -1)
                    {
                        var resourceTypeId = (short)0;
                        var resourceCount = 0;
                        var totalCount = 0;
                        var split = definition.Split(";");
                        resourceTypeId = short.Parse(split[0]);
                        var minId = long.Parse(split[1]);
                        var maxId = long.Parse(split[2]);
                        var suffix = split.Length > 3 ? split[3] : null;
                        (resourceCount, totalCount) = Copy(worker, resourceTypeId, jobId, minId, maxId);

                        Target.CompleteJob(jobId, false, version, resourceCount);
                    }
                    else
                    {
                        Thread.Sleep(10000);
                    }
                }
                catch (Exception e)
                {
                    Target.LogEvent($"Copy", "Error", $"{worker}.{jobId}", text: e.ToString());
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

                    if (jobId != -1)
                    {
                        Target.CompleteJob(jobId, true, version, null);
                    }
                }
            }
        }

        private (int resourceCnt, int totalCnt) Copy(int thread, short resourceTypeId, long jobId, long minId, long maxId)
        {
            var sw = Stopwatch.StartNew();
            var resources = GetData(_ => new Resource(_), resourceTypeId, minId, maxId);
            var referenceSearchParams = GetData(_ => new ReferenceSearchParam(_), resourceTypeId, minId, maxId);
            var tokenSearchParams = GetData(_ => new TokenSearchParam(_), resourceTypeId, minId, maxId);
            var compartmentAssignments = GetData(_ => new CompartmentAssignment(_), resourceTypeId, minId, maxId);
            var tokenTexts = GetData(_ => new TokenText(_), resourceTypeId, minId, maxId);
            var dateTimeSearchParams = GetData(_ => new DateTimeSearchParam(_), resourceTypeId, minId, maxId);
            var tokenQuantityCompositeSearchParams = GetData(_ => new TokenQuantityCompositeSearchParam(_), resourceTypeId, minId, maxId);
            var quantitySearchParams = GetData(_ => new QuantitySearchParam(_), resourceTypeId, minId, maxId);
            var stringSearchParams = GetData(_ => new StringSearchParam(_), resourceTypeId, minId, maxId);
            var tokenTokenCompositeSearchParams = GetData(_ => new TokenTokenCompositeSearchParam(_), resourceTypeId, minId, maxId);
            var tokenStringCompositeSearchParams = GetData(_ => new TokenStringCompositeSearchParam(_), resourceTypeId, minId, maxId);
            var rows = 0;
            if (_writesEnabled)
            {
                rows = Target.InsertResources(true, resources, referenceSearchParams, tokenSearchParams, compartmentAssignments, tokenTexts, dateTimeSearchParams, tokenQuantityCompositeSearchParams, quantitySearchParams, stringSearchParams, tokenTokenCompositeSearchParams, tokenStringCompositeSearchParams, false);
            }

            Console.WriteLine($"Copy.{thread}.{jobId}.{resourceTypeId}.{minId}: completed at {DateTime.Now:s}, elapsed={sw.Elapsed.TotalSeconds:N0} sec.");
            return (resources.Count, rows);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "No user input")]
        private IList<T> GetData<T>(Func<SqlDataReader, T> toT, short resourceTypeId, long minId, long maxId)
        {
            List<T> results = null;
            using var cmd = new SqlCommand($"SELECT * FROM dbo.{typeof(T).Name} WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId BETWEEN @MinId AND @MaxId ORDER BY ResourceSurrogateId") { CommandTimeout = 600 };
            cmd.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
            cmd.Parameters.AddWithValue("@MinId", minId);
            cmd.Parameters.AddWithValue("@MaxId", maxId);
            SqlUtils.SqlService.ExecuteSqlWithRetries(
                _sourceConnectionString,
                cmd,
                cmdInt =>
                {
                    results = new List<T>();
                    using var reader = cmdInt.ExecuteReader();
                    while (reader.Read())
                    {
                        results.Add(toT(reader));
                    }

                    reader.NextResult();
                });
            return results;
        }

        private string GetSourceConnectionString()
        {
            using var conn = Target.GetConnection();
            using var cmd = new SqlCommand("SELECT Char FROM dbo.Parameters WHERE Id = 'Copy.SourceConnectionString'", conn);
            var str = cmd.ExecuteScalar();
            return str == null ? null : (string)str;
        }

        private int GetWorkers()
        {
            using var conn = Target.GetConnection();
            using var cmd = new SqlCommand("SELECT convert(int,Number) FROM dbo.Parameters WHERE Id = 'Copy.Workers'", conn);
            var threads = cmd.ExecuteScalar();
            return threads == null ? 1 : (int)threads;
        }

        private bool GetWritesEnabled()
        {
            using var conn = Target.GetConnection();
            using var cmd = new SqlCommand("SELECT convert(bit,Number) FROM dbo.Parameters WHERE Id = 'Copy.WritesEnabled'", conn);
            var flag = cmd.ExecuteScalar();
            return flag != null && (bool)flag;
        }
    }
}
