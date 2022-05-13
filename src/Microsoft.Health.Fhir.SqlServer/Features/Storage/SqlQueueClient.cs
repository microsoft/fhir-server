// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.TaskManagement;
using TaskStatus = Microsoft.Health.TaskManagement.TaskStatus;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SqlQueueClient : IQueueClient
    {
        private SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private SchemaInformation _schemaInformation;

        public SqlQueueClient(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            SchemaInformation schemaInformation)
        {
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));

            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _schemaInformation = schemaInformation;
        }

        public async Task CancelTaskAsync(byte queueType, long? groupId, long? jobId, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.PutJobCancelation.PopulateCommand(sqlCommandWrapper, queueType, groupId, jobId);
                await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        public async Task CompleteTaskAsync(TaskInfo taskInfo, bool requestCancellationOnFailure, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.PutJobStatus.PopulateCommand(sqlCommandWrapper, taskInfo.QueueType, taskInfo.Id, taskInfo.Version, taskInfo.Status == TaskStatus.Failed, taskInfo.Data ?? 0, taskInfo.Result, requestCancellationOnFailure);
                try
                {
                    await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
                }
                catch (SqlException sqlEx)
                {
                    if (sqlEx.Number == SqlErrorCodes.PreconditionFailed)
                    {
                        throw new TaskNotExistException(sqlEx.Message);
                    }

                    throw;
                }
            }
        }

        public async Task<TaskInfo> DequeueAsync(byte queueType, byte startPartitionId, string worker, int heartbeatTimeoutSec, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.DequeueJob.PopulateCommand(sqlCommandWrapper, queueType, startPartitionId, worker, heartbeatTimeoutSec);
                SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                TaskInfo taskInfo = await sqlDataReader.ReadTaskInfoAsync(cancellationToken);
                if (taskInfo != null)
                {
                    taskInfo.QueueType = queueType;
                }

                return taskInfo;
            }
        }

        public async Task<IEnumerable<TaskInfo>> EnqueueAsync(byte queueType, string[] definitions, long? groupId, bool forceOneActiveJobGroup, bool isCompleted, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.EnqueueJobs.PopulateCommand(sqlCommandWrapper, queueType, definitions.Select(d => new StringListRow(d)), groupId, forceOneActiveJobGroup, isCompleted);
                try
                {
                    SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                    return await sqlDataReader.ReadTaskInfosAsync(cancellationToken);
                }
                catch (SqlException sqlEx)
                {
                    if (sqlEx.State == 127)
                    {
                        throw new TaskConflictException(sqlEx.Message);
                    }

                    throw;
                }
            }
        }

        public async Task<IEnumerable<TaskInfo>> GetTaskByGroupIdAsync(byte queueType, long groupId, bool returnDefinition, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.GetJobs.PopulateCommand(sqlCommandWrapper, queueType, null, null, groupId, returnDefinition);
                SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                return await sqlDataReader.ReadTaskInfosAsync(cancellationToken);
            }
        }

        public async Task<TaskInfo> GetTaskByIdAsync(byte queueType, long taskId, bool returnDefinition, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.GetJobs.PopulateCommand(sqlCommandWrapper, queueType, taskId, null, null, returnDefinition);
                SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                return await sqlDataReader.ReadTaskInfoAsync(cancellationToken);
            }
        }

        public async Task<IEnumerable<TaskInfo>> GetTaskByIdsAsync(byte queueType, long[] taskIds, bool returnDefinition, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.GetJobs.PopulateCommand(sqlCommandWrapper, queueType, null, taskIds.Select(i => new BigintListRow(i)), null, returnDefinition);
                SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                return await sqlDataReader.ReadTaskInfosAsync(cancellationToken);
            }
        }

        public bool IsInitialized()
        {
            return _schemaInformation.Current != null;
        }

        public async Task<TaskInfo> KeepAliveTaskAsync(TaskInfo taskInfo, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                try
                {
                    VLatest.PutJobHeartbeat.PopulateCommand(sqlCommandWrapper, taskInfo.QueueType, taskInfo.Id, taskInfo.Version, taskInfo.Data, taskInfo.Result);
                    await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                    return await GetTaskByIdAsync(taskInfo.QueueType, taskInfo.Id, false, cancellationToken);
                }
                catch (SqlException sqlEx)
                {
                    if (sqlEx.Number == SqlErrorCodes.PreconditionFailed)
                    {
                        throw new TaskNotExistException(sqlEx.Message);
                    }

                    throw;
                }
            }
        }
    }
}
