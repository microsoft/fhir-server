// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
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
        public async Task<IReadOnlyCollection<ExportJobOutcome>> AcquireExportJobsAsync(ushort maximumNumberOfConcurrentJobsAllowed, TimeSpan jobHeartbeatTimeoutThreshold, CancellationToken cancellationToken)
        {
            // We will consider a job to be stale if its timestamp is smaller than or equal to this.
            DateTimeOffset expirationTime = Clock.UtcNow - jobHeartbeatTimeoutThreshold;

            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            using (SqlTransaction transaction = sqlConnectionWrapper.SqlConnection.BeginTransaction(IsolationLevel.Serializable))
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                sqlCommand.Transaction = transaction;

                int numberOfRunningJobs = await GetNumberOfRunningJobs(sqlCommand, expirationTime, cancellationToken);

                // Calculate the maximum number of available jobs we can pick up given how many are already running.
                var limit = maximumNumberOfConcurrentJobsAllowed - numberOfRunningJobs;

                IList<ExportJobOutcome> availableJobs = await GetAvailableJobs(sqlCommand, limit, expirationTime, cancellationToken);

                if (availableJobs.Count > 0)
                {
                    await UpdateAvailableJobs(sqlCommand, availableJobs, cancellationToken);
                }

                transaction.Commit();

                return new ReadOnlyCollection<ExportJobOutcome>(availableJobs);
            }
        }

        public async Task<ExportJobOutcome> CreateExportJobAsync(ExportJobRecord jobRecord, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
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
            // TODO: Implement this.
            return Task.FromResult(new ExportJobOutcome(jobRecord, eTag));
        }

        private static async Task<int> GetNumberOfRunningJobs(SqlCommand sqlCommand, DateTimeOffset expirationTime, CancellationToken cancellationToken)
        {
            sqlCommand.CommandText = $"SELECT COUNT(*) FROM dbo.ExportJob WITH (TABLOCKX) WHERE Status = 'Running' AND HeartbeatDateTime > '{expirationTime}'";

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

        private static async Task<List<ExportJobOutcome>> GetAvailableJobs(SqlCommand sqlCommand, int limit, DateTimeOffset expirationTime, CancellationToken cancellationToken)
        {
            // Available jobs are ones that are queued or stale.
            // TODO: Is this the best way to prioritize which jobs are picked up?
            sqlCommand.CommandText = $"SELECT TOP {limit} * FROM dbo.ExportJob WHERE (Status = 'Queued' OR (Status = 'Running' AND HeartbeatDateTime <= '{expirationTime}')) ORDER BY HeartbeatDateTime, QueuedDateTime";

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

                    var jobRecord = JsonConvert.DeserializeObject<ExportJobRecord>(rawJobRecord);
                    string rowVersion = GetByteArrayValue(rowVersionBytes);

                    exportJobOutcomes.Add(new ExportJobOutcome(jobRecord, WeakETag.FromVersionId(rowVersion)));
                }

                return exportJobOutcomes;
            }
        }

        private static string GetByteArrayValue(byte[] bytes)
        {
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            return BitConverter.ToInt32(bytes, 0).ToString();
        }

        private static async Task UpdateAvailableJobs(SqlCommand sqlCommand, IList<ExportJobOutcome> exportJobOutcomes, CancellationToken cancellationToken)
        {
            string availableJobsIds = string.Join(",", exportJobOutcomes.Select(outcome => $"'{outcome.JobRecord.Id}'"));
            string versions = string.Join(",", exportJobOutcomes.Select(outcome => $"CONVERT(TIMESTAMP, {outcome.ETag.VersionId})"));

            string status = OperationStatus.Running.ToString();
            DateTimeOffset heartbeatTimeStamp = Clock.UtcNow;

            // Update the job records in the export table.
            sqlCommand.CommandText = $"UPDATE dbo.ExportJob SET Status = '{status}', HeartbeatDateTime = '{heartbeatTimeStamp}', RawJobRecord = REPLACE(RawJobRecord, '\"status\":1', '\"status\":2') WHERE Id IN ({availableJobsIds}) AND JobVersion IN ({versions})";
            await sqlCommand.ExecuteNonQueryAsync(cancellationToken);

            // Update the returned job records objects.
            exportJobOutcomes.ToList().ForEach(outcome => outcome.JobRecord.Status = OperationStatus.Running);
        }
    }
}
