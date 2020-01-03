// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.SqlClient;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.IO;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal class SqlServerFhirOperationDataStore : IFhirOperationDataStore
    {
        internal static readonly Encoding ResourceEncoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);

        private readonly ILogger<SqlServerFhirOperationDataStore> _logger;
        private readonly RecyclableMemoryStreamManager _memoryStreamManager;
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;

        public SqlServerFhirOperationDataStore(SqlConnectionWrapperFactory sqlConnectionWrapperFactory, ILogger<SqlServerFhirOperationDataStore> logger)
        {
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _logger = logger;

            _memoryStreamManager = new RecyclableMemoryStreamManager();
        }

        // TODO: Use parameterized queries.
        // TODO: Should a new SQL command be created in each one of the methods that this method calls? Or should there be one for all?
        public async Task<IReadOnlyCollection<ExportJobOutcome>> AcquireExportJobsAsync(ushort maximumNumberOfConcurrentJobsAllowed, TimeSpan jobHeartbeatTimeoutThreshold, CancellationToken cancellationToken)
        {
            // We will consider a job to be stale if its timestamp is smaller than or equal to this.
            DateTimeOffset expirationTime = Clock.UtcNow - jobHeartbeatTimeoutThreshold;

            using (SqlConnectionWrapper sqlConnectionWrapper =
                _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            {
                int numberOfRunningJobs = await GetNumberOfRunningJobs(expirationTime, sqlConnectionWrapper, cancellationToken);

                // Calculate the maximum number of available jobs we can pick up given how many are already running.
                var limit = maximumNumberOfConcurrentJobsAllowed - numberOfRunningJobs;

                IList<ExportJobOutcome> availableJobs = await GetAvailableJobs(sqlConnectionWrapper, limit, expirationTime, cancellationToken);

                await UpdateAvailableJobs(sqlConnectionWrapper, availableJobs, cancellationToken);

                return new ReadOnlyCollection<ExportJobOutcome>(availableJobs);
            }
        }

        public async Task<ExportJobOutcome> CreateExportJobAsync(ExportJobRecord jobRecord, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            ////using (var stream = new RecyclableMemoryStream(_memoryStreamManager))
            ////using (var gzipStream = new GZipStream(stream, CompressionMode.Compress))
            ////using (var writer = new StreamWriter(gzipStream, ResourceEncoding))
            {
                // TODO: raw job resource should be a stream.
                ////var rawJobRecord = JsonConvert.SerializeObject(jobRecord);
                ////writer.Write(rawJobRecord);
                ////writer.Flush();

                ////stream.Seek(0, 0);

                V1.CreateExportJob.PopulateCommand(
                    sqlCommand,
                    jobRecord.Id,
                    jobRecord.Status.ToString(),
                    jobRecord.QueuedTime,
                    JsonConvert.SerializeObject(jobRecord));

                var rowVersion = (int?)await sqlCommand.ExecuteScalarAsync(cancellationToken);

                if (rowVersion == null)
                {
                    throw new OperationFailedException(string.Format(Core.Resources.OperationFailed, OperationsConstants.Export, Resources.NullRowVersion), HttpStatusCode.InternalServerError);
                }

                return new ExportJobOutcome(jobRecord, WeakETag.FromVersionId(rowVersion.ToString()));
            }
        }

        public Task<ExportJobOutcome> GetExportJobByIdAsync(string id, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<ExportJobOutcome> GetExportJobByHashAsync(string hash, CancellationToken cancellationToken)
        {
            // TODO: Implement this method (returning null for now to allow the create method to run).
            return Task.FromResult<ExportJobOutcome>(null);
        }

        public Task<ExportJobOutcome> UpdateExportJobAsync(ExportJobRecord jobRecord, WeakETag eTag, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        private static async Task<int> GetNumberOfRunningJobs(DateTimeOffset expirationTime, SqlConnectionWrapper sqlConnectionWrapper, CancellationToken cancellationToken)
        {
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                sqlCommand.CommandText = $"SELECT COUNT(*) FROM dbo.ExportJob WHERE Status = 'Running' AND HeartbeatDateTime > '{expirationTime}'";

                int numberOfRunningJobs = 0;

                try
                {
                    numberOfRunningJobs = (int)await sqlCommand.ExecuteScalarAsync(cancellationToken);
                }
                catch (SqlException)
                {
                    // TODO
                }

                return numberOfRunningJobs;
            }
        }

        private static async Task<List<ExportJobOutcome>> GetAvailableJobs(SqlConnectionWrapper sqlConnectionWrapper, int limit, DateTimeOffset expirationTime, CancellationToken cancellationToken)
        {
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                // Available jobs are ones that are queued or stale.
                // TODO: The heartbeat of the queued jobs will be NULL - should we be ordering by a different column?
                sqlCommand.CommandText = $"SELECT TOP {limit} * FROM dbo.ExportJob WHERE (Status = 'Queued' OR (Status = 'Running' AND HeartbeatDateTime <= '{expirationTime}')) ORDER BY HeartbeatDateTime ASC";

                using (SqlDataReader reader = await sqlCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                {
                    var exportJobOutcomes = new List<ExportJobOutcome>();

                    while (await reader.ReadAsync(cancellationToken))
                    {
                        (string id, string status, DateTimeOffset? heartbeatDateTime, DateTimeOffset queuedDateTime, string rawJobRecord, byte[] rowVersionBytes) = reader.ReadRow(
                            V1.ExportJob.Id,
                            V1.ExportJob.Status,
                            V1.ExportJob.HeartbeatDateTime,
                            V1.ExportJob.QueuedDateTime,
                            V1.ExportJob.RawJobRecord,
                            V1.ExportJob.JobVersion);

                        // TODO: raw job resource should be a stream.
                        ////    string rawJobRecord;
                        ////    ExportJobRecord jobRecord;

                        ////    using (rawJobRecordStream)
                        ////    using (var gzipStream = new GZipStream(rawJobRecordStream, CompressionMode.Decompress))
                        ////    using (var streamReader = new StreamReader(gzipStream, SqlServerFhirDataStore.ResourceEncoding))
                        ////    {
                        ////        rawJobRecord = await streamReader.ReadToEndAsync();
                        ////        jobRecord = JsonConvert.DeserializeObject<ExportJobRecord>(rawJobRecord);
                        ////    }

                        var jobRecord = JsonConvert.DeserializeObject<ExportJobRecord>(rawJobRecord);

                        // TODO: This is not properly parsing the row version.
                        int bufferIndex = 0;
                        string rowVersion = Encoding.Default.GetString(rowVersionBytes, bufferIndex, rowVersionBytes.Length);

                        exportJobOutcomes.Add(new ExportJobOutcome(jobRecord, WeakETag.FromVersionId(rowVersion)));
                    }

                    return exportJobOutcomes;
                }
            }
        }

        private static async Task UpdateAvailableJobs(SqlConnectionWrapper sqlConnectionWrapper, IList<ExportJobOutcome> exportJobOutcomes, CancellationToken cancellationToken)
        {
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                // TODO: Need to check version / handle concurrency.
                foreach (ExportJobOutcome exportJobOutcome in exportJobOutcomes)
                {
                    ExportJobRecord availableJob = exportJobOutcome.JobRecord;

                    availableJob.Status = OperationStatus.Running;

                    string status = availableJob.Status.ToString();
                    DateTimeOffset heartbeatTimeStamp = Clock.UtcNow;
                    string serializedJob = JsonConvert.SerializeObject(availableJob);

                    sqlCommand.CommandText = $"UPDATE dbo.ExportJob SET Status = '{status}', HeartbeatDateTime = '{heartbeatTimeStamp}', RawJobRecord = '{serializedJob}' WHERE Id = '{availableJob.Id}'";
                    await sqlCommand.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }
    }
}
