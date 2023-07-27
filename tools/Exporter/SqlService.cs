// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.JobManagement;
using Microsoft.Health.SqlServer;

namespace Microsoft.Health.Internal.Fhir.Exporter
{
    internal sealed class SqlService
    {
        private readonly string _connectionString;
        private readonly SqlRetryService _sqlRetryService;
        private readonly SqlQueueClient _queue;
        private byte _queueType = (byte)QueueType.Export;

        public SqlService(string connectionString)
        {
            _connectionString = connectionString;
            ISqlConnectionBuilder iSqlConnectionBuilder = new SqlConnectionBuilder(_connectionString);
            _sqlRetryService = SqlRetryService.GetInstance(iSqlConnectionBuilder);
            _queue = new SqlQueueClient(_sqlRetryService, NullLogger<SqlQueueClient>.Instance);
        }

        public string ConnectionString => _connectionString;

        public SqlRetryService SqlRetryService => _sqlRetryService;

        internal void DequeueJob(out long groupId, out long jobId, out long version, out short? resourceTypeId, out string minSurIdOrUrl, out string maxSurId)
        {
            var jobInfo = _queue.DequeueAsync(_queueType, "A", 600, CancellationToken.None).Result;
            resourceTypeId = null;
            minSurIdOrUrl = string.Empty;
            maxSurId = string.Empty;
            groupId = -1;
            jobId = -1;
            version = -1;
            if (jobInfo != null)
            {
                groupId = jobInfo.GroupId;
                jobId = jobInfo.Id;
                version = jobInfo.Version;
                var split = jobInfo.Definition.Split(";");
                resourceTypeId = short.Parse(split[0]);
                minSurIdOrUrl = split[1];
                maxSurId = split[2];
            }
        }

        internal void CompleteJob(long jobId, bool failed, long version, int? resourceCount = null)
        {
            var jobInfo = new JobInfo() { Id = jobId, Version = version, Data = resourceCount, Status = failed ? JobStatus.Failed : JobStatus.Completed };
            _queue.CompleteJobAsync(jobInfo, false, CancellationToken.None).Wait();
        }

        internal IEnumerable<byte[]> GetDataBytes(short resourceTypeId, long minId, long maxId)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqlCommand("dbo.GetResourcesByTypeAndSurrogateIdRange", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 600 };
            cmd.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
            cmd.Parameters.AddWithValue("@StartId", minId);
            cmd.Parameters.AddWithValue("@EndId", maxId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                yield return reader.GetSqlBytes(0).Value;
            }
        }

        internal IEnumerable<(int UnitId, long StartId, long EndId, int ResourceCount)> GetSurrogateIdRanges(short resourceTypeId, long startId, long endId, int unitSize)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqlCommand("dbo.GetResourceSurrogateIdRanges", conn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 3600 };
            cmd.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
            cmd.Parameters.AddWithValue("@StartId", startId);
            cmd.Parameters.AddWithValue("@EndId", endId);
            cmd.Parameters.AddWithValue("@UnitSize", unitSize);
            cmd.Parameters.AddWithValue("@NumberOfRanges", (int)(2e9 / unitSize));
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                yield return (reader.GetInt32(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt32(3));
            }
        }

        internal short GetResourceTypeId(string resourceType)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqlCommand("SELECT ResourceTypeId FROM dbo.ResourceType B WHERE B.Name = @ResourceType", conn) { CommandTimeout = 60 };
            cmd.Parameters.AddWithValue("@ResourceType", resourceType);
            using var reader = cmd.ExecuteReader();
            var resourceTypeId = (short)-1;
            while (reader.Read())
            {
                resourceTypeId = reader.GetInt16(0);
            }

            return resourceTypeId;
        }

        internal IEnumerable<string> GetDataStrings(short resourceTypeId, long minId, long maxId)
        {
            foreach (var res in GetDataBytes(resourceTypeId, minId, maxId))
            {
                using var mem = new MemoryStream(res);
                yield return Health.Fhir.Store.Export.CompressedRawResourceConverterCopy.ReadCompressedRawResource(mem);
            }
        }
    }
}
