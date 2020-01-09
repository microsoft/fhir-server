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
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal class SqlServerFhirOperationDataStore : IFhirOperationDataStore
    {
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly ILogger<SqlServerFhirOperationDataStore> _logger;
        private readonly V1.UpdateExportJobsTvpGenerator<List<ExportJobOutcome>> _updateExportJobsTvpGenerator;

        public SqlServerFhirOperationDataStore(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            V1.UpdateExportJobsTvpGenerator<List<ExportJobOutcome>> updateExportJobsTvpGenerator,
            ILogger<SqlServerFhirOperationDataStore> logger)
        {
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(updateExportJobsTvpGenerator, nameof(updateExportJobsTvpGenerator));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _updateExportJobsTvpGenerator = updateExportJobsTvpGenerator;
            _logger = logger;
        }

        // TODO: Use parameterized queries or move to stored procedure.
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

                List<ExportJobOutcome> availableJobs = await GetAvailableJobs(sqlCommand, limit, expirationTime, cancellationToken);

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
            // TODO: Implement this method.
            return Task.FromResult(new ExportJobOutcome(jobRecord, eTag));
        }

        private async Task<int> GetNumberOfRunningJobs(SqlCommand sqlCommand, DateTimeOffset expirationTime, CancellationToken cancellationToken)
        {
            sqlCommand.CommandText = $"SELECT COUNT(*) FROM dbo.ExportJob WITH (TABLOCKX) WHERE Status = 'Running' AND HeartbeatDateTime > '{expirationTime}'";

            int numberOfRunningJobs = 0;

            try
            {
                numberOfRunningJobs = (int)await sqlCommand.ExecuteScalarAsync(cancellationToken);
            }
            catch (SqlException)
            {
                // TODO: Add error handling to acquire export methods.
            }

            return numberOfRunningJobs;
        }

        private async Task<List<ExportJobOutcome>> GetAvailableJobs(SqlCommand sqlCommand, int limit, DateTimeOffset expirationTime, CancellationToken cancellationToken)
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
                    string rowVersion = RowVersionConverter.GetVersionAsDecimalString(rowVersionBytes);

                    exportJobOutcomes.Add(new ExportJobOutcome(jobRecord, WeakETag.FromVersionId(rowVersion)));
                }

                return exportJobOutcomes;
            }
        }

        // TODO: Add error handling to acquire export methods.
        private async Task UpdateAvailableJobs(SqlCommand sqlCommand, List<ExportJobOutcome> exportJobOutcomes, CancellationToken cancellationToken)
        {
            string status = OperationStatus.Running.ToString();
            DateTimeOffset heartbeatTimeStamp = Clock.UtcNow;

            V1.UpdateExportJobs.PopulateCommand(
                sqlCommand,
                status,
                heartbeatTimeStamp,
                _updateExportJobsTvpGenerator.Generate(exportJobOutcomes));

            await sqlCommand.ExecuteNonQueryAsync(cancellationToken);

            // Update the returned job records objects.
            exportJobOutcomes.ToList().ForEach(outcome => outcome.JobRecord.Status = OperationStatus.Running);
        }
    }
}
