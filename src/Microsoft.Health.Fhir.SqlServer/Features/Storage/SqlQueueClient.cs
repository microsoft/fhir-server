// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.JobManagement;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using JobStatus = Microsoft.Health.JobManagement.JobStatus;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SqlQueueClient : IQueueClient
    {
        private readonly SchemaInformation _schemaInformation;
        private readonly ISqlRetryService _sqlRetryService;
        private readonly ILogger<SqlQueueClient> _logger;

        public SqlQueueClient(SchemaInformation schemaInformation, ISqlRetryService sqlRetryService, ILogger<SqlQueueClient> logger)
        {
            _schemaInformation = EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public SqlQueueClient(ISqlRetryService sqlRetryService, ILogger<SqlQueueClient> logger)
        {
            _sqlRetryService = EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public async Task CancelJobByGroupIdAsync(byte queueType, long groupId, CancellationToken cancellationToken)
        {
            try
            {
                using var cmd = new SqlCommand("dbo.PutJobCancelation") { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@QueueType", queueType);
                cmd.Parameters.AddWithValue("@GroupId", groupId);
                await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CancelJobByGroupIdAsync failed.");
                if (ex.IsRetriable())
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
                using var cmd = new SqlCommand("dbo.PutJobCancelation") { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@QueueType", queueType);
                cmd.Parameters.AddWithValue("@JobId", jobId);
                await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CancelJobByIdAsync failed.");
                if (ex.IsRetriable())
                {
                    throw new RetriableJobException(ex.Message, ex);
                }

                throw;
            }
        }

        public virtual async Task CompleteJobAsync(JobInfo jobInfo, bool requestCancellationOnFailure, CancellationToken cancellationToken)
        {
            using var sqlCommand = new SqlCommand() { CommandText = "dbo.PutJobStatus", CommandType = CommandType.StoredProcedure };
            sqlCommand.Parameters.AddWithValue("@QueueType", jobInfo.QueueType);
            sqlCommand.Parameters.AddWithValue("@JobId", jobInfo.Id);
            sqlCommand.Parameters.AddWithValue("@Version", jobInfo.Version);
            sqlCommand.Parameters.AddWithValue("@Failed", jobInfo.Status == JobStatus.Failed);
            sqlCommand.Parameters.AddWithValue("@Data", jobInfo.Data.HasValue ? jobInfo.Data.Value : DBNull.Value);
            sqlCommand.Parameters.AddWithValue("@FinalResult", jobInfo.Result != null ? jobInfo.Result : DBNull.Value);
            sqlCommand.Parameters.AddWithValue("@RequestCancellationOnFailure", requestCancellationOnFailure);

            await _sqlRetryService.ExecuteSql(
                sqlCommand,
                async (cmd, cancel) =>
                {
                    try
                    {
                        await cmd.ExecuteNonQueryAsync(cancel);
                    }
                    catch (SqlException sqlEx)
                    {
                        if (sqlEx.Number == SqlErrorCodes.PreconditionFailed)
                        {
                            throw new JobNotExistException(sqlEx.Message, sqlEx);
                        }

                        throw;
                    }
                },
                _logger,
                null,
                cancellationToken);
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

        public async Task<JobInfo> DequeueAsync(byte queueType, string worker, int heartbeatTimeoutSec, CancellationToken cancellationToken, long? jobId = null, bool checkTimeoutJobsOnly = false)
        {
            using var sqlCommand = new SqlCommand() { CommandText = "dbo.DequeueJob", CommandType = CommandType.StoredProcedure };
            sqlCommand.Parameters.AddWithValue("@QueueType", queueType);
            sqlCommand.Parameters.AddWithValue("@Worker", worker);
            sqlCommand.Parameters.AddWithValue("@HeartbeatTimeoutSec", heartbeatTimeoutSec);
            sqlCommand.Parameters.AddWithValue("@CheckTimeoutJobs", checkTimeoutJobsOnly);
            if (jobId.HasValue)
            {
                sqlCommand.Parameters.AddWithValue("@InputJobId", jobId.Value);
            }

            var jobInfos = await sqlCommand.ExecuteReaderAsync(_sqlRetryService, JobInfoExtensions.LoadJobInfo, _logger, cancellationToken);
            var jobInfo = jobInfos.Count == 0 ? null : jobInfos[0];
            if (jobInfo != null)
            {
                jobInfo.QueueType = queueType;
            }

            return jobInfo;
        }

        public async Task<IReadOnlyList<JobInfo>> EnqueueAsync(byte queueType, string[] definitions, long? groupId, bool forceOneActiveJobGroup, bool isCompleted, CancellationToken cancellationToken)
        {
            using var sqlCommand = new SqlCommand() { CommandText = "dbo.EnqueueJobs", CommandType = CommandType.StoredProcedure, CommandTimeout = 300 };
            sqlCommand.Parameters.AddWithValue("@QueueType", queueType);
            new StringListTableValuedParameterDefinition("@Definitions").AddParameter(sqlCommand.Parameters, definitions.Select(d => new StringListRow(d)));
            if (groupId.HasValue)
            {
                sqlCommand.Parameters.AddWithValue("@GroupId", groupId.Value);
            }

            sqlCommand.Parameters.AddWithValue("@ForceOneActiveJobGroup", forceOneActiveJobGroup);
            sqlCommand.Parameters.AddWithValue("@IsCompleted", isCompleted);

            try
            {
                return await sqlCommand.ExecuteReaderAsync(_sqlRetryService, JobInfoExtensions.LoadJobInfo, _logger, cancellationToken);
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

        public async Task<IReadOnlyList<JobInfo>> GetJobByGroupIdAsync(byte queueType, long groupId, bool returnDefinition, CancellationToken cancellationToken)
        {
            using var sqlCommand = new SqlCommand();
            PopulateGetJobsCommand(sqlCommand, queueType, null, null, groupId, returnDefinition);
            return await sqlCommand.ExecuteReaderAsync(_sqlRetryService, JobInfoExtensions.LoadJobInfo, _logger, cancellationToken, "GetJobByGroupIdAsync failed.");
        }

        public async Task<JobInfo> GetJobByIdAsync(byte queueType, long jobId, bool returnDefinition, CancellationToken cancellationToken)
        {
            using var sqlCommand = new SqlCommand();
            PopulateGetJobsCommand(sqlCommand, queueType, jobId, returnDefinition: returnDefinition);
            var jobs = await sqlCommand.ExecuteReaderAsync(_sqlRetryService, JobInfoExtensions.LoadJobInfo, _logger, cancellationToken, "GetJobByIdAsync failed.");
            return jobs.Count == 0 ? null : jobs[0];
        }

        public async Task<IReadOnlyList<JobInfo>> GetJobsByIdsAsync(byte queueType, long[] jobIds, bool returnDefinition, CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand();
            PopulateGetJobsCommand(cmd, queueType, jobIds: jobIds, returnDefinition: returnDefinition);
            return await cmd.ExecuteReaderAsync(_sqlRetryService, JobInfoExtensions.LoadJobInfo, _logger, cancellationToken, "GetJobsByIdsAsync failed.");
        }

        public bool IsInitialized()
        {
            if (_schemaInformation == null)
            {
                throw new InvalidOperationException("This method cannot be called with schema information = null");
            }

            return _schemaInformation.Current != null && _schemaInformation.Current != 0;
        }

        public async Task ArchiveJobsAsync(byte queueType, CancellationToken cancellationToken)
        {
            using var cmd = new SqlCommand("dbo.ArchiveJobs") { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddWithValue("@QueueType", queueType);
            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
        }

        public async Task<bool> PutJobHeartbeatAsync(JobInfo jobInfo, CancellationToken cancellationToken)
        {
            var cancel = false;
            try
            {
                using var cmd = new SqlCommand("dbo.PutJobHeartbeat") { CommandType = CommandType.StoredProcedure };
                cmd.Parameters.AddWithValue("@QueueType", jobInfo.QueueType);
                cmd.Parameters.AddWithValue("@JobId", jobInfo.Id);
                cmd.Parameters.AddWithValue("@Version", jobInfo.Version);
                if (jobInfo.Data.HasValue)
                {
                    cmd.Parameters.AddWithValue("@Data", jobInfo.Data.Value);
                }
                else
                {
                    cmd.Parameters.AddWithValue("@Data", DBNull.Value);
                }

                if (jobInfo.Result != null)
                {
                    cmd.Parameters.AddWithValue("@CurrentResult", jobInfo.Result);
                }

                var cancelParam = new SqlParameter("@CancelRequested", SqlDbType.Bit) { Direction = ParameterDirection.Output };
                cmd.Parameters.Add(cancelParam);
                await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
                cancel = (bool)cancelParam.Value;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to put job heartbeat.");
            }

            return cancel;
        }

        private static void PopulateGetJobsCommand(SqlCommand cmd, byte queueType, long? jobId = null, IEnumerable<long> jobIds = null, long? groupId = null, bool? returnDefinition = null)
        {
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "dbo.GetJobs";
            cmd.Parameters.AddWithValue("@QueueType", queueType);
            if (jobId.HasValue)
            {
                cmd.Parameters.AddWithValue("@JobId", jobId.Value);
            }

            if (jobIds != null)
            {
                new BigintListTableValuedParameterDefinition("@JobIds").AddParameter(cmd.Parameters, jobIds.Select(_ => new BigintListRow(_)));
            }

            if (groupId.HasValue)
            {
                cmd.Parameters.AddWithValue("@GroupId", groupId.Value);
            }

            if (returnDefinition.HasValue)
            {
                cmd.Parameters.AddWithValue("@ReturnDefinition", returnDefinition.Value);
            }
        }
    }
}
