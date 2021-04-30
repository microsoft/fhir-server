// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Client;
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

        public SqlServerTaskConsumer(
            IOptions<TaskHostingConfiguration> taskHostingConfiguration,
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            ILogger<SqlServerTaskConsumer> logger)
        {
            EnsureArg.IsNotNull(taskHostingConfiguration, nameof(taskHostingConfiguration));
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _taskHostingConfiguration = taskHostingConfiguration.Value;
            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _logger = logger;
        }

        public async Task<TaskInfo> CompleteAsync(string taskId, TaskResultData resultData, string runId, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                try
                {
                    VLatest.CompleteTask.PopulateCommand(sqlCommandWrapper, taskId, JsonConvert.SerializeObject(resultData), runId);
                    SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                    if (!sqlDataReader.Read())
                    {
                        return null;
                    }

                    var taskInfoTable = VLatest.TaskInfo;
                    (_, string queueId, short status, short taskTypeId, string runIdResult, bool isCanceled, short retryCount, DateTime? heartbeatDateTime, string inputData, string taskContext, string result) = sqlDataReader.ReadRow(
                        taskInfoTable.TaskId,
                        taskInfoTable.QueueId,
                        taskInfoTable.Status,
                        taskInfoTable.TaskTypeId,
                        taskInfoTable.RunId,
                        taskInfoTable.IsCanceled,
                        taskInfoTable.RetryCount,
                        taskInfoTable.HeartbeatDateTime,
                        taskInfoTable.InputData,
                        taskInfoTable.TaskContext,
                        taskInfoTable.Result);

                    return new TaskInfo()
                    {
                        TaskId = taskId,
                        QueueId = queueId,
                        Status = (TaskStatus)status,
                        TaskTypeId = taskTypeId,
                        RunId = runIdResult,
                        IsCanceled = isCanceled,
                        RetryCount = retryCount,
                        HeartbeatDateTime = heartbeatDateTime,
                        InputData = inputData,
                        Context = taskContext,
                        Result = result,
                    };
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

        public async Task<IReadOnlyCollection<TaskInfo>> GetNextMessagesAsync(short count, int taskHeartbeatTimeoutThresholdInSeconds, CancellationToken cancellationToken)
        {
            List<TaskInfo> output = new List<TaskInfo>();

            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.GetNextTask.PopulateCommand(sqlCommandWrapper, _taskHostingConfiguration.QueueId, count, taskHeartbeatTimeoutThresholdInSeconds);
                SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                var taskInfoTable = VLatest.TaskInfo;
                while (sqlDataReader.Read())
                {
                    (string id, string queueId, short status, short taskTypeId, string runId, bool isCanceled, short retryCount, DateTime? heartbeatDateTime, string inputData, string taskContext, string result) = sqlDataReader.ReadRow(
                        taskInfoTable.TaskId,
                        taskInfoTable.QueueId,
                        taskInfoTable.Status,
                        taskInfoTable.TaskTypeId,
                        taskInfoTable.RunId,
                        taskInfoTable.IsCanceled,
                        taskInfoTable.RetryCount,
                        taskInfoTable.HeartbeatDateTime,
                        taskInfoTable.InputData,
                        taskInfoTable.TaskContext,
                        taskInfoTable.Result);

                    TaskInfo taskInfo = new TaskInfo()
                    {
                        TaskId = id,
                        QueueId = queueId,
                        Status = (TaskStatus)status,
                        TaskTypeId = taskTypeId,
                        RunId = runId,
                        IsCanceled = isCanceled,
                        RetryCount = retryCount,
                        HeartbeatDateTime = heartbeatDateTime,
                        InputData = inputData,
                        Context = taskContext,
                        Result = result,
                    };

                    output.Add(taskInfo);
                }
            }

            return output;
        }

        public async Task<TaskInfo> KeepAliveAsync(string taskId, string runId, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                try
                {
                    VLatest.TaskKeepAlive.PopulateCommand(sqlCommandWrapper, taskId, runId);
                    SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                    if (!sqlDataReader.Read())
                    {
                        return null;
                    }

                    var taskInfoTable = VLatest.TaskInfo;
                    (_, string queueId, short status, short taskTypeId, string runIdResult, bool isCanceled, short retryCount, DateTime? heartbeatDateTime, string inputData, string taskContext, string result) = sqlDataReader.ReadRow(
                        taskInfoTable.TaskId,
                        taskInfoTable.QueueId,
                        taskInfoTable.Status,
                        taskInfoTable.TaskTypeId,
                        taskInfoTable.RunId,
                        taskInfoTable.IsCanceled,
                        taskInfoTable.RetryCount,
                        taskInfoTable.HeartbeatDateTime,
                        taskInfoTable.InputData,
                        taskInfoTable.TaskContext,
                        taskInfoTable.Result);

                    return new TaskInfo()
                    {
                        TaskId = taskId,
                        QueueId = queueId,
                        Status = (TaskStatus)status,
                        TaskTypeId = taskTypeId,
                        RunId = runIdResult,
                        IsCanceled = isCanceled,
                        RetryCount = retryCount,
                        HeartbeatDateTime = heartbeatDateTime,
                        InputData = inputData,
                        Context = taskContext,
                        Result = result,
                    };
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

        public async Task<TaskInfo> ResetAsync(string taskId, TaskResultData resultData, string runId, short maxRetryCount, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                try
                {
                    VLatest.ResetTask.PopulateCommand(sqlCommandWrapper, taskId, runId, JsonConvert.SerializeObject(resultData), maxRetryCount);
                    SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                    if (!sqlDataReader.Read())
                    {
                        return null;
                    }

                    var taskInfoTable = VLatest.TaskInfo;
                    (_, string queueId, short status, short taskTypeId, string runIdResult, bool isCanceled, short retryCount, DateTime? heartbeatDateTime, string inputData, string taskContext, string result) = sqlDataReader.ReadRow(
                        taskInfoTable.TaskId,
                        taskInfoTable.QueueId,
                        taskInfoTable.Status,
                        taskInfoTable.TaskTypeId,
                        taskInfoTable.RunId,
                        taskInfoTable.IsCanceled,
                        taskInfoTable.RetryCount,
                        taskInfoTable.HeartbeatDateTime,
                        taskInfoTable.InputData,
                        taskInfoTable.TaskContext,
                        taskInfoTable.Result);

                    TaskStatus taskStatus = (TaskStatus)status;
                    return taskStatus == TaskStatus.Completed
                        ? throw new TaskAlreadyCompletedException("Task already completed or reach max retry count.")
                        : new TaskInfo()
                        {
                            TaskId = taskId,
                            QueueId = queueId,
                            Status = taskStatus,
                            TaskTypeId = taskTypeId,
                            RunId = runIdResult,
                            IsCanceled = isCanceled,
                            RetryCount = retryCount,
                            HeartbeatDateTime = heartbeatDateTime,
                            InputData = inputData,
                            Context = taskContext,
                            Result = result,
                        };
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
