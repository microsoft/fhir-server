// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.TaskManagement;
using Newtonsoft.Json;
using TaskStatus = Microsoft.Health.TaskManagement.TaskStatus;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SqlServerTaskConsumer : ITaskConsumer
    {
        private TaskHostingConfiguration _taskHostingConfiguration;
        private SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private ILogger<SqlServerTaskConsumer> _logger;
        private readonly SchemaInformation _schemaInformation;

        public SqlServerTaskConsumer(
            IOptions<TaskHostingConfiguration> taskHostingConfiguration,
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            SchemaInformation schemaInformation,
            ILogger<SqlServerTaskConsumer> logger)
        {
            EnsureArg.IsNotNull(taskHostingConfiguration, nameof(taskHostingConfiguration));
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _taskHostingConfiguration = taskHostingConfiguration.Value;
            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _schemaInformation = schemaInformation;
            _logger = logger;
        }

        public bool EnsureInitializedAsync()
        {
            return _schemaInformation.Current != null;
        }

        public async Task CompleteAsync(TaskInfo taskInfo, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                try
                {
                    VLatest.PutJobStatus.PopulateCommand(sqlCommandWrapper, (byte)taskInfo.QueueType, taskInfo.Id, taskInfo.Version, taskInfo.Status == TaskStatus.Failed, taskInfo.Data ?? 0, taskInfo.Result, taskInfo.CancelRequested);
                    await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);
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

        public async Task<TaskInfo> DequeueAsync(QueueType queueType, byte startPartitionId, string worker, int heartbeatTimeoutSec, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.DequeueJob.PopulateCommand(sqlCommandWrapper, (byte)queueType, startPartitionId, worker, heartbeatTimeoutSec);

                SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);
                return sqlDataReader.ReadTaskInfo();
            }
        }

        public async Task<TaskInfo> CompleteAsync(string taskId, TaskResultData taskResultData, string runId, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                try
                {
                    VLatest.CompleteTask.PopulateCommand(sqlCommandWrapper, taskId, JsonConvert.SerializeObject(taskResultData), runId);
                    SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                    return sqlDataReader.ReadTaskInfo();
                }
                catch (SqlException sqlEx)
                {
                    if (sqlEx.Number == SqlErrorCodes.NotFound)
                    {
                        throw new TaskNotExistException(sqlEx.Message);
                    }

                    throw;
                }
            }
        }

        public async Task<TaskInfo> GetNextMessagesAsync(int taskHeartbeatTimeoutThresholdInSeconds, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                string queueId = _taskHostingConfiguration.QueueId;
                if (_schemaInformation.Current >= SchemaVersionConstants.RemoveCountForGexNextTaskStoredProcedure)
                {
                    VLatest.GetNextTask.PopulateCommand(sqlCommandWrapper, queueId, taskHeartbeatTimeoutThresholdInSeconds);
                }
                else
                {
                    V28.GetNextTask.PopulateCommand(sqlCommandWrapper, queueId, 1, taskHeartbeatTimeoutThresholdInSeconds);
                }

                SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);
                return sqlDataReader.ReadTaskInfo();
            }
        }

        public async Task<TaskInfo> KeepAliveAsync(string taskId, string runId, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                try
                {
                    VLatest.TaskKeepAlive.PopulateCommand(sqlCommandWrapper, taskId, runId);
                    SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                    return sqlDataReader.ReadTaskInfo();
                }
                catch (SqlException sqlEx)
                {
                    if (sqlEx.Number == SqlErrorCodes.NotFound)
                    {
                        throw new TaskNotExistException(sqlEx.Message);
                    }

                    throw;
                }
            }
        }

        public async Task<TaskInfo> ResetAsync(string taskId, TaskResultData taskResultData, string runId, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                try
                {
                    VLatest.ResetTask.PopulateCommand(sqlCommandWrapper, taskId, runId, JsonConvert.SerializeObject(taskResultData));
                    SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                    TaskInfo taskInfo = sqlDataReader.ReadTaskInfo();

                    TaskStatus taskStatus = (TaskStatus)taskInfo.Status;
                    return taskStatus == TaskStatus.Completed
                        ? throw new TaskAlreadyCompletedException("Task already completed or reach max retry count.")
                        : taskInfo;
                }
                catch (SqlException sqlEx)
                {
                    if (sqlEx.Number == SqlErrorCodes.NotFound)
                    {
                        throw new TaskNotExistException(sqlEx.Message);
                    }

                    throw;
                }
            }
        }
    }
}
