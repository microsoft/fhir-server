// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.JobManagement;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using JobStatus = Microsoft.Health.JobManagement.JobStatus;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SqlQueueClient : IQueueClient
    {
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly SchemaInformation _schemaInformation;
        private readonly ILogger<SqlQueueClient> _logger;

        public SqlQueueClient(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            SchemaInformation schemaInformation,
            ILogger<SqlQueueClient> logger)
        {
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _schemaInformation = schemaInformation;
            _logger = logger;
        }

        public async Task CancelJobByGroupIdAsync(byte queueType, long groupId, CancellationToken cancellationToken)
        {
            try
            {
                using SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true);
                using SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand();

                VLatest.PutJobCancelation.PopulateCommand(sqlCommandWrapper, queueType, groupId, null);
                await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CancelJobByGroupIdAsync failed.");
                if (ex.IsRetryable())
                {
                    throw new RetriableJobException(ex.Message, ex);
                }

                throw;
            }
        }

        public async Task CancelJobByIdAsync(byte queueType, long jobId, CancellationToken cancellationToken)
        {
            try
            {
                using SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true);
                using SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand();

                VLatest.PutJobCancelation.PopulateCommand(sqlCommandWrapper, queueType, null, jobId);
                await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CancelJobByIdAsync failed.");
                if (ex.IsRetryable())
                {
                    throw new RetriableJobException(ex.Message, ex);
                }

                throw;
            }
        }

        public virtual async Task CompleteJobAsync(JobInfo jobInfo, bool requestCancellationOnFailure, CancellationToken cancellationToken)
        {
            try
            {
                using SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true);
                using SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand();

                // cannot use VLatest as it incorrectly sends nulls
                sqlCommandWrapper.CommandType = System.Data.CommandType.StoredProcedure;
                sqlCommandWrapper.CommandText = "dbo.PutJobStatus";
                sqlCommandWrapper.Parameters.AddWithValue("@QueueType", jobInfo.QueueType);
                sqlCommandWrapper.Parameters.AddWithValue("@JobId", jobInfo.Id);
                sqlCommandWrapper.Parameters.AddWithValue("@Version", jobInfo.Version);
                sqlCommandWrapper.Parameters.AddWithValue("@Failed", jobInfo.Status == JobStatus.Failed);
                sqlCommandWrapper.Parameters.AddWithValue("@Data", jobInfo.Data.HasValue ? jobInfo.Data.Value : DBNull.Value);
                sqlCommandWrapper.Parameters.AddWithValue("@FinalResult", jobInfo.Result != null ? jobInfo.Result : DBNull.Value);
                sqlCommandWrapper.Parameters.AddWithValue("@RequestCancellationOnFailure", requestCancellationOnFailure);

                try
                {
                    await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (SqlException sqlEx)
                {
                    if (sqlEx.Number == SqlErrorCodes.PreconditionFailed)
                    {
                        throw new JobNotExistException(sqlEx.Message);
                    }

                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CompleteJobAsync failed.");
                if (ex.IsRetryable())
                {
                    throw new RetriableJobException(ex.Message, ex);
                }

                throw;
            }
        }

        public async Task<IReadOnlyCollection<JobInfo>> DequeueJobsAsync(byte queueType, int numberOfJobsToDequeue, string worker, int heartbeatTimeoutSec, CancellationToken cancellationToken)
        {
            var dequeuedJobs = new List<JobInfo>();

            while (dequeuedJobs.Count < numberOfJobsToDequeue)
            {
                var jobInfo = await DequeueAsync(queueType, worker, heartbeatTimeoutSec, cancellationToken);

                if (jobInfo != null)
                {
                    dequeuedJobs.Add(jobInfo);
                }
                else
                {
                    // No more jobs in queue
                    break;
                }
            }

            return dequeuedJobs;
        }

        public async Task<JobInfo> DequeueAsync(byte queueType, string worker, int heartbeatTimeoutSec, CancellationToken cancellationToken, long? jobId = null)
        {
            try
            {
                using SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true);
                using SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand();

                // cannot use VLatest as it incorrectly asks for optional @InputJobId
                sqlCommandWrapper.CommandText = "dbo.DequeueJob";
                sqlCommandWrapper.CommandType = System.Data.CommandType.StoredProcedure;
                sqlCommandWrapper.Parameters.AddWithValue("@QueueType", queueType);
                sqlCommandWrapper.Parameters.AddWithValue("@Worker", worker);
                sqlCommandWrapper.Parameters.AddWithValue("@HeartbeatTimeoutSec", heartbeatTimeoutSec);
                if (jobId.HasValue)
                {
                    sqlCommandWrapper.Parameters.AddWithValue("@InputJobId", jobId.Value);
                }

                await using SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);
                JobInfo jobInfo = await sqlDataReader.ReadJobInfoAsync(cancellationToken);
                if (jobInfo != null)
                {
                    jobInfo.QueueType = queueType;
                }

                return jobInfo;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DequeueAsync failed.");
                if (ex.IsRetryable())
                {
                    throw new RetriableJobException(ex.Message, ex);
                }

                throw;
            }
        }

        public async Task<IReadOnlyList<JobInfo>> EnqueueAsync(byte queueType, string[] definitions, long? groupId, bool forceOneActiveJobGroup, bool isCompleted, CancellationToken cancellationToken)
        {
            try
            {
                using SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true);
                using SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand();

                // cannot use VLatest as it does not understand optional parameters
                sqlCommandWrapper.CommandType = System.Data.CommandType.StoredProcedure;
                sqlCommandWrapper.CommandText = "dbo.EnqueueJobs";
                sqlCommandWrapper.Parameters.AddWithValue("@QueueType", queueType);
                new StringListTableValuedParameterDefinition("@Definitions").AddParameter(sqlCommandWrapper.Parameters, definitions.Select(d => new StringListRow(d)));
                if (groupId.HasValue)
                {
                    sqlCommandWrapper.Parameters.AddWithValue("@GroupId", groupId.Value);
                }

                sqlCommandWrapper.Parameters.AddWithValue("@ForceOneActiveJobGroup", forceOneActiveJobGroup);
                sqlCommandWrapper.Parameters.AddWithValue("@IsCompleted", isCompleted);

                try
                {
                    await using SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                    return await sqlDataReader.ReadJobInfosAsync(cancellationToken);
                }
                catch (SqlException sqlEx)
                {
                    if (sqlEx.State == 127)
                    {
                        throw new JobManagement.JobConflictException(sqlEx.Message);
                    }

                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EnqueueAsync failed.");
                if (ex.IsRetryable())
                {
                    throw new RetriableJobException(ex.Message, ex);
                }

                throw;
            }
        }

        public async Task<IReadOnlyList<JobInfo>> GetJobByGroupIdAsync(byte queueType, long groupId, bool returnDefinition, CancellationToken cancellationToken)
        {
            try
            {
                using SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true);
                using SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand();

                VLatest.GetJobs.PopulateCommand(sqlCommandWrapper, queueType, null, null, groupId, returnDefinition);
                await using SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                return await sqlDataReader.ReadJobInfosAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetJobByGroupIdAsync failed.");
                if (ex.IsRetryable())
                {
                    throw new RetriableJobException(ex.Message, ex);
                }

                throw;
            }
        }

        public async Task<JobInfo> GetJobByIdAsync(byte queueType, long jobId, bool returnDefinition, CancellationToken cancellationToken)
        {
            try
            {
                using SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true);
                using SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand();

                VLatest.GetJobs.PopulateCommand(sqlCommandWrapper, queueType, jobId, null, null, returnDefinition);
                await using SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                return await sqlDataReader.ReadJobInfoAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetJobByIdAsync failed.");
                if (ex.IsRetryable())
                {
                    throw new RetriableJobException(ex.Message, ex);
                }

                throw;
            }
        }

        public async Task<IReadOnlyList<JobInfo>> GetJobsByIdsAsync(byte queueType, long[] jobIds, bool returnDefinition, CancellationToken cancellationToken)
        {
            try
            {
                using SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true);
                using SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand();

                VLatest.GetJobs.PopulateCommand(sqlCommandWrapper, queueType, null, jobIds.Select(i => new BigintListRow(i)), null, returnDefinition);
                await using SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                return await sqlDataReader.ReadJobInfosAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetJobsByIdsAsync failed.");
                if (ex.IsRetryable())
                {
                    throw new RetriableJobException(ex.Message, ex);
                }

                throw;
            }
        }

        public bool IsInitialized()
        {
            return _schemaInformation.Current != null && _schemaInformation.Current != 0;
        }

        public async Task ArchiveJobsAsync(byte queueType, CancellationToken cancellationToken)
        {
            using SqlConnectionWrapper conn = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, false);
            using SqlCommandWrapper cmd = conn.CreateRetrySqlCommand();
            VLatest.ArchiveJobs.PopulateCommand(cmd, queueType);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task ExecuteJobWithHeartbeats(byte queueType, long jobId, long version, Func<CancellationTokenSource, Task> action, TimeSpan heartbeatPeriod, CancellationTokenSource cancellationTokenSource)
        {
            EnsureArg.IsNotNull(action, nameof(action));

            await using (new Timer(_ => PutJobHeartbeat(queueType, jobId, version, cancellationTokenSource), null, TimeSpan.FromSeconds(RandomNumberGenerator.GetInt32(100) / 100.0 * heartbeatPeriod.TotalSeconds), heartbeatPeriod))
            {
                await action(cancellationTokenSource);
            }
        }

        public async Task<string> ExecuteJobWithHeartbeats(JobInfo jobInfo, Func<CancellationTokenSource, Task<string>> action, TimeSpan heartbeatPeriod, CancellationTokenSource cancellationTokenSource)
        {
            EnsureArg.IsNotNull(action, nameof(action));

            await using (new Timer(_ => PutJobHeartbeatHeavy(jobInfo, cancellationTokenSource), null, TimeSpan.FromSeconds(RandomNumberGenerator.GetInt32(100) / 100.0 * heartbeatPeriod.TotalSeconds), heartbeatPeriod))
            {
                return await action(cancellationTokenSource);
            }
        }

        private void PutJobHeartbeat(byte queueType, long jobId, long version, CancellationTokenSource cancellationTokenSource)
        {
            var cancel = PutJobHeartbeat(new JobInfo { QueueType = queueType, Id = jobId, Version = version }, cancellationTokenSource.Token).Result;
            if (cancel)
            {
                cancellationTokenSource.Cancel();
            }
        }

        private void PutJobHeartbeatHeavy(JobInfo jobInfo, CancellationTokenSource cancellationTokenSource)
        {
            var cancel = PutJobHeartbeat(jobInfo, cancellationTokenSource.Token).Result;
            if (cancel)
            {
                cancellationTokenSource.Cancel();
            }
        }

        private async Task<bool> PutJobHeartbeat(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            var cancel = false;
            try
            {
                using SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true);
                using SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateNonRetrySqlCommand();
                VLatest.PutJobHeartbeat.PopulateCommand(sqlCommandWrapper, jobInfo.QueueType, jobInfo.Id, jobInfo.Version, jobInfo.Data, jobInfo.Result, false);
                await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
                cancel = VLatest.PutJobHeartbeat.GetOutputs(sqlCommandWrapper) ?? false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to put job heartbeat.");
            }

            return cancel;
        }
    }
}
