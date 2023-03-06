// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.JobManagement;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Storage;
using JobStatus = Microsoft.Health.JobManagement.JobStatus;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SqlQueueClient : IQueueClient
    {
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly SchemaInformation _schemaInformation;
        private readonly ISqlRetryService _sqlRetryService;
        private readonly ILogger<SqlQueueClient> _logger;

        private static readonly GetJobsProcedure GetJobs = new GetJobsProcedure();

        public SqlQueueClient(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            SchemaInformation schemaInformation,
            ISqlRetryService sqlRetryService,
            ILogger<SqlQueueClient> logger)
        {
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _schemaInformation = schemaInformation;
            _sqlRetryService = sqlRetryService;
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
                using SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true);
                using SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand();

                VLatest.PutJobCancelation.PopulateCommand(sqlCommandWrapper, queueType, null, jobId);
                await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
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
            using SqlCommand sqlCommand = new SqlCommand();

            // cannot use VLatest as it incorrectly sends nulls
            sqlCommand.CommandType = System.Data.CommandType.StoredProcedure;
            sqlCommand.CommandText = "dbo.PutJobStatus";
            sqlCommand.Parameters.AddWithValue("@QueueType", jobInfo.QueueType);
            sqlCommand.Parameters.AddWithValue("@JobId", jobInfo.Id);
            sqlCommand.Parameters.AddWithValue("@Version", jobInfo.Version);
            sqlCommand.Parameters.AddWithValue("@Failed", jobInfo.Status == JobStatus.Failed);
            sqlCommand.Parameters.AddWithValue("@Data", jobInfo.Data.HasValue ? jobInfo.Data.Value : DBNull.Value);
            sqlCommand.Parameters.AddWithValue("@FinalResult", jobInfo.Result != null ? jobInfo.Result : DBNull.Value);
            sqlCommand.Parameters.AddWithValue("@RequestCancellationOnFailure", requestCancellationOnFailure);

            await _sqlRetryService.ExecuteSql(
                sqlCommand,
                async (sqlCommand, cancellationToken) =>
                {
                    try
                    {
                        await sqlCommand.ExecuteNonQueryAsync(cancellationToken);
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
                "CompleteJobAsync failed.",
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

        public async Task<JobInfo> DequeueAsync(byte queueType, string worker, int heartbeatTimeoutSec, CancellationToken cancellationToken, long? jobId = null)
        {
            using SqlCommand sqlCommand = new SqlCommand();

            // cannot use VLatest as it incorrectly asks for optional @InputJobId
            sqlCommand.CommandText = "dbo.DequeueJob";
            sqlCommand.CommandType = System.Data.CommandType.StoredProcedure;
            sqlCommand.Parameters.AddWithValue("@QueueType", queueType);
            sqlCommand.Parameters.AddWithValue("@Worker", worker);
            sqlCommand.Parameters.AddWithValue("@HeartbeatTimeoutSec", heartbeatTimeoutSec);
            if (jobId.HasValue)
            {
                sqlCommand.Parameters.AddWithValue("@InputJobId", jobId.Value);
            }

            JobInfo jobInfo = await _sqlRetryService.ExecuteSqlDataReaderFirstRow(
                sqlCommand,
                JobInfoExtensions.LoadJobInfo,
                _logger,
                "DequeueAsync failed.",
                cancellationToken);
            if (jobInfo != null)
            {
                jobInfo.QueueType = queueType;
            }

            return jobInfo;
        }

        public async Task<IReadOnlyList<JobInfo>> EnqueueAsync(byte queueType, string[] definitions, long? groupId, bool forceOneActiveJobGroup, bool isCompleted, CancellationToken cancellationToken)
        {
            using SqlCommand sqlCommand = new SqlCommand();

            // cannot use VLatest as it does not understand optional parameters
            sqlCommand.CommandType = System.Data.CommandType.StoredProcedure;
            sqlCommand.CommandText = "dbo.EnqueueJobs";
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
                return await _sqlRetryService.ExecuteSqlDataReader(
                    sqlCommand,
                    JobInfoExtensions.LoadJobInfo,
                    _logger,
                    "EnqueueJobs failed.",
                    cancellationToken);
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
            using SqlCommand sqlCommand = new SqlCommand();

            GetJobs.PopulateCommand(sqlCommand, queueType, null, null, groupId, returnDefinition);

            return await _sqlRetryService.ExecuteSqlDataReader(
                sqlCommand,
                JobInfoExtensions.LoadJobInfo,
                _logger,
                "GetJobByGroupIdAsync failed.",
                cancellationToken);
        }

        public async Task<JobInfo> GetJobByIdAsync(byte queueType, long jobId, bool returnDefinition, CancellationToken cancellationToken)
        {
            using SqlCommand sqlCommand = new SqlCommand();

            GetJobs.PopulateCommand(sqlCommand, queueType, jobId, null, null, returnDefinition);

            return await _sqlRetryService.ExecuteSqlDataReaderFirstRow(
                sqlCommand,
                JobInfoExtensions.LoadJobInfo,
                _logger,
                "GetJobByIdAsync failed.",
                cancellationToken);
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
                if (ex.IsRetriable())
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

        public async Task<bool> PutJobHeartbeatAsync(JobInfo jobInfo, CancellationToken cancellationToken)
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

        // Class copied from src\Microsoft.Health.Fhir.SqlServer\Features\Schema\Model\VLatest.Generated.cs .
        // Autogenerated version in VLatest.Generated.cs uses SqlCommandWrapper instead of SqlCommand as the input parameter, so here
        // we have the version that uses SqlCommand since we are depreciating SqlCommandWrapper.
        private class GetJobsProcedure : StoredProcedure
        {
            private readonly ParameterDefinition<byte> _queueType = new ParameterDefinition<byte>("@QueueType", global::System.Data.SqlDbType.TinyInt, false);
            private readonly ParameterDefinition<long?> _jobId = new ParameterDefinition<long?>("@JobId", global::System.Data.SqlDbType.BigInt, true);
            private readonly BigintListTableValuedParameterDefinition _jobIds = new BigintListTableValuedParameterDefinition("@JobIds");
            private readonly ParameterDefinition<long?> _groupId = new ParameterDefinition<long?>("@GroupId", global::System.Data.SqlDbType.BigInt, true);

            internal GetJobsProcedure()
                : base("dbo.GetJobs")
            {
            }

            public void PopulateCommand(SqlCommand sqlCommand, byte queueType, long? jobId, global::System.Collections.Generic.IEnumerable<BigintListRow> jobIds, long? groupId, bool? returnDefinition)
            {
                sqlCommand.CommandType = global::System.Data.CommandType.StoredProcedure;
                sqlCommand.CommandText = "dbo.GetJobs";
                _queueType.AddParameter(sqlCommand.Parameters, queueType);
                _jobId.AddParameter(sqlCommand.Parameters, jobId);
                _jobIds.AddParameter(sqlCommand.Parameters, jobIds);
                _groupId.AddParameter(sqlCommand.Parameters, groupId);
            }
        }
    }
}
