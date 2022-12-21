// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
#pragma warning disable SA1107 // Code should not contain multiple statements on one line

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Store.Database;
using Microsoft.Health.Fhir.Store.Utils;

namespace Microsoft.Health.Fhir.Store.WatchDogs
{
    public class CopyWorkerNotSharded
    {
        private int _workers = 1;
        private bool _writesEnabled = false;
        private int _maxRetries = 10;
        private byte _queueType = 3;
        private bool _identifiersOnly = false;
        private HashSet<short> _identifiers = new HashSet<short>();

        public CopyWorkerNotSharded(string connectionString)
        {
            Target = new SqlService(connectionString);
            var sourceConnectionString = GetSourceConnectionString();
            if (sourceConnectionString == null)
            {
                throw new ArgumentException("sourceConnectionString == null");
            }

            Source = new SqlService(sourceConnectionString);

            _workers = GetWorkers();
            _writesEnabled = GetWritesEnabled();
            _identifiersOnly = GetIdentifiersOnly();
            if (_identifiersOnly)
            {
                _identifiers = GetIdentifiers();
            }

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

        public SqlService Source { get; private set; }

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
                    var dequeue = Target.DequeueJob(_queueType);
                    jobId = dequeue.JobId;
                    version = dequeue.Version;
                    if (jobId != -1)
                    {
                        var resourceTypeId = (short)0;
                        var resourceCount = 0;
                        var totalCount = 0;
                        var split = dequeue.Definition.Split(";");
                        resourceTypeId = short.Parse(split[0]);
                        var minId = long.Parse(split[1]);
                        var maxId = long.Parse(split[2]);
                        var suffix = split.Length > 3 ? split[3] : null;
                        (resourceCount, totalCount) = Copy(worker, resourceTypeId, jobId, minId, maxId);

                        Target.CompleteJob(_queueType, jobId, false, version, resourceCount, totalCount);
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
                        Target.CompleteJob(_queueType, jobId, true, version);
                    }
                }
            }
        }

        private (int resourceCnt, int totalCnt) Copy(int thread, short resourceTypeId, long jobId, long minId, long maxId)
        {
            var sw = Stopwatch.StartNew();
            var surrIdToSequence = new Dictionary<long, int>();
            var count = 0;
            var resources = Source.GetData(_ => new Resource(_), resourceTypeId, minId, maxId);
            foreach (var resource in resources)
            {
                count++;
                surrIdToSequence.Add(resource.ResourceSurrogateId, count);
            }

            resources = resources.Select(_ => { _.ResourceSurrogateId = surrIdToSequence[_.ResourceSurrogateId]; return _; }).ToList();

            if (_identifiersOnly)
            {
                resources = resources.Select(_ => { _.RawResource = new[] { (byte)1, (byte)1 }; return _; }).ToList();
            }

            var referenceSearchParams = Source.GetData(_ => new ReferenceSearchParam(_), resourceTypeId, minId, maxId);
            referenceSearchParams = referenceSearchParams.Select(_ => { _.ResourceSurrogateId = surrIdToSequence[_.ResourceSurrogateId]; return _; }).ToList();
            if (referenceSearchParams.Count == 0)
            {
                referenceSearchParams = null;
            }

            var tokenSearchParams = Source.GetData(_ => new TokenSearchParam(_), resourceTypeId, minId, maxId);
            tokenSearchParams = tokenSearchParams.Select(_ => { _.ResourceSurrogateId = surrIdToSequence[_.ResourceSurrogateId]; return _; }).ToList();
            if (_identifiersOnly)
            {
                tokenSearchParams = tokenSearchParams.Where(_ => _identifiers.Contains(_.SearchParamId)).ToList();
            }

            if (tokenSearchParams.Count == 0)
            {
                tokenSearchParams = null;
            }

            var compartmentAssignments = _identifiersOnly ? new List<CompartmentAssignment>() : Source.GetData(_ => new CompartmentAssignment(_), resourceTypeId, minId, maxId);
            compartmentAssignments = compartmentAssignments.Select(_ => { _.ResourceSurrogateId = surrIdToSequence[_.ResourceSurrogateId]; return _; }).ToList();
            if (compartmentAssignments.Count == 0)
            {
                compartmentAssignments = null;
            }

            var tokenTexts = _identifiersOnly ? new List<TokenText>() : Source.GetData(_ => new TokenText(_), resourceTypeId, minId, maxId);
            tokenTexts = tokenTexts.Select(_ => { _.ResourceSurrogateId = surrIdToSequence[_.ResourceSurrogateId]; return _; }).ToList();
            if (tokenTexts.Count == 0)
            {
                tokenTexts = null;
            }

            var dateTimeSearchParams = _identifiersOnly ? new List<DateTimeSearchParam>() : Source.GetData(_ => new DateTimeSearchParam(_), resourceTypeId, minId, maxId);
            dateTimeSearchParams = dateTimeSearchParams.Select(_ => { _.ResourceSurrogateId = surrIdToSequence[_.ResourceSurrogateId]; return _; }).ToList();
            if (dateTimeSearchParams.Count == 0)
            {
                dateTimeSearchParams = null;
            }

            var tokenQuantityCompositeSearchParams = _identifiersOnly ? new List<TokenQuantityCompositeSearchParam>() : Source.GetData(_ => new TokenQuantityCompositeSearchParam(_), resourceTypeId, minId, maxId);
            tokenQuantityCompositeSearchParams = tokenQuantityCompositeSearchParams.Select(_ => { _.ResourceSurrogateId = surrIdToSequence[_.ResourceSurrogateId]; return _; }).ToList();
            if (tokenQuantityCompositeSearchParams.Count == 0)
            {
                tokenQuantityCompositeSearchParams = null;
            }

            var quantitySearchParams = _identifiersOnly ? new List<QuantitySearchParam>() : Source.GetData(_ => new QuantitySearchParam(_), resourceTypeId, minId, maxId);
            quantitySearchParams = quantitySearchParams.Select(_ => { _.ResourceSurrogateId = surrIdToSequence[_.ResourceSurrogateId]; return _; }).ToList();
            if (quantitySearchParams.Count == 0)
            {
                quantitySearchParams = null;
            }

            var stringSearchParams = _identifiersOnly ? new List<StringSearchParam>() : Source.GetData(_ => new StringSearchParam(_), resourceTypeId, minId, maxId);
            stringSearchParams = stringSearchParams.Select(_ => { _.ResourceSurrogateId = surrIdToSequence[_.ResourceSurrogateId]; return _; }).ToList();
            if (stringSearchParams.Count == 0)
            {
                stringSearchParams = null;
            }

            var tokenTokenCompositeSearchParams = _identifiersOnly ? new List<TokenTokenCompositeSearchParam>() : Source.GetData(_ => new TokenTokenCompositeSearchParam(_), resourceTypeId, minId, maxId);
            tokenTokenCompositeSearchParams = tokenTokenCompositeSearchParams.Select(_ => { _.ResourceSurrogateId = surrIdToSequence[_.ResourceSurrogateId]; return _; }).ToList();
            if (tokenTokenCompositeSearchParams.Count == 0)
            {
                tokenTokenCompositeSearchParams = null;
            }

            var tokenStringCompositeSearchParams = _identifiersOnly ? new List<TokenStringCompositeSearchParam>() : Source.GetData(_ => new TokenStringCompositeSearchParam(_), resourceTypeId, minId, maxId);
            tokenStringCompositeSearchParams = tokenStringCompositeSearchParams.Select(_ => { _.ResourceSurrogateId = surrIdToSequence[_.ResourceSurrogateId]; return _; }).ToList();
            if (tokenStringCompositeSearchParams.Count == 0)
            {
                tokenStringCompositeSearchParams = null;
            }

            var rows = 0;
            if (_writesEnabled)
            {
                rows = Target.MergeResources(resources, referenceSearchParams, tokenSearchParams, compartmentAssignments, tokenTexts, dateTimeSearchParams, tokenQuantityCompositeSearchParams, quantitySearchParams, stringSearchParams, tokenTokenCompositeSearchParams, tokenStringCompositeSearchParams, false);
            }

            Console.WriteLine($"Copy.{thread}.{jobId}.{resourceTypeId}.{minId}: completed at {DateTime.Now:s}, elapsed={sw.Elapsed.TotalSeconds:N0} sec.");
            return (resources.Count, rows);
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

        private bool GetIdentifiersOnly()
        {
            using var conn = Target.GetConnection();
            using var cmd = new SqlCommand("SELECT convert(bit,Number) FROM dbo.Parameters WHERE Id = 'Copy.IdentifiersOnly'", conn);
            var flag = cmd.ExecuteScalar();
            return flag != null && (bool)flag;
        }

        private HashSet<short> GetIdentifiers()
        {
            var results = new HashSet<short>();
            using var conn = Source.GetConnection();
            using var cmd = new SqlCommand("SELECT SearchParamId FROM dbo.SearchParam WHERE Uri LIKE '%identi%'", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(reader.GetInt16(0));
            }

            return results;
        }
    }
}
