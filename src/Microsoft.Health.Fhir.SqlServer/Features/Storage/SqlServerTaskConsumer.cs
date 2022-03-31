// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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

        public async Task<TaskInfo> CompleteAsync(string taskId, TaskResultData taskResultData, string runId, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                try
                {
                    VLatest.CompleteTask.PopulateCommand(sqlCommandWrapper, taskId, JsonConvert.SerializeObject(taskResultData), runId);
                    SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                    if (!sqlDataReader.Read())
                    {
                        return null;
                    }

                    var taskInfoTable = VLatest.TaskInfo;
                    _ = sqlDataReader.Read(taskInfoTable.TaskId, 0);
                    string queueId = sqlDataReader.Read(taskInfoTable.QueueId, 1);
                    short status = sqlDataReader.Read(taskInfoTable.Status, 2);
                    short taskTypeId = sqlDataReader.Read(taskInfoTable.TaskTypeId, 3);
                    string taskRunId = sqlDataReader.Read(taskInfoTable.RunId, 4);
                    bool isCanceled = sqlDataReader.Read(taskInfoTable.IsCanceled, 5);
                    short retryCount = sqlDataReader.Read(taskInfoTable.RetryCount, 6);
                    short maxRetryCount = sqlDataReader.Read(taskInfoTable.MaxRetryCount, 7);
                    DateTime? heartbeatDateTime = sqlDataReader.Read(taskInfoTable.HeartbeatDateTime, 8);
                    string inputData = sqlDataReader.Read(taskInfoTable.InputData, 9);
                    string taskContext = sqlDataReader.Read(taskInfoTable.TaskContext, 10);
                    string result = sqlDataReader.Read(taskInfoTable.Result, 11);

                    return new TaskInfo()
                    {
                        TaskId = taskId,
                        QueueId = queueId,
                        Status = (TaskStatus)status,
                        TaskTypeId = taskTypeId,
                        RunId = taskRunId,
                        IsCanceled = isCanceled,
                        RetryCount = retryCount,
                        MaxRetryCount = maxRetryCount,
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

        public async Task<TaskInfo> GetNextMessagesAsync(int taskHeartbeatTimeoutThresholdInSeconds, CancellationToken cancellationToken)
        {
            TaskInfo taskInfo = null;
            try
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
                        V28.GetNextTask.PopulateCommand(sqlCommandWrapper, queueId, taskHeartbeatTimeoutThresholdInSeconds);
                    }

                    SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);
                    var taskInfoTable = VLatest.TaskInfo;
                    if (!sqlDataReader.Read())
                    {
                        return null;
                    }

                    string id = sqlDataReader.Read(taskInfoTable.TaskId, 0);
                    _ = sqlDataReader.Read(taskInfoTable.QueueId, 1);
                    short status = sqlDataReader.Read(taskInfoTable.Status, 2);
                    short taskTypeId = sqlDataReader.Read(taskInfoTable.TaskTypeId, 3);
                    string taskRunId = sqlDataReader.Read(taskInfoTable.RunId, 4);
                    bool isCanceled = sqlDataReader.Read(taskInfoTable.IsCanceled, 5);
                    short retryCount = sqlDataReader.Read(taskInfoTable.RetryCount, 6);
                    short maxRetryCount = sqlDataReader.Read(taskInfoTable.MaxRetryCount, 7);
                    DateTime? heartbeatDateTime = sqlDataReader.Read(taskInfoTable.HeartbeatDateTime, 8);
                    string inputData = sqlDataReader.Read(taskInfoTable.InputData, 9);
                    string taskContext = sqlDataReader.Read(taskInfoTable.TaskContext, 10);
                    string result = sqlDataReader.Read(taskInfoTable.Result, 11);

                    taskInfo = new TaskInfo()
                    {
                        TaskId = id,
                        QueueId = queueId,
                        Status = (TaskStatus)status,
                        TaskTypeId = taskTypeId,
                        RunId = taskRunId,
                        IsCanceled = isCanceled,
                        RetryCount = retryCount,
                        MaxRetryCount = maxRetryCount,
                        HeartbeatDateTime = heartbeatDateTime,
                        InputData = inputData,
                        Context = taskContext,
                        Result = result,
                    };
                }
            }
            catch (SqlException e) when (e.Number == 2812)
            {
                _logger.LogWarning(e, "Schema is not initialized - {ex.Message}", e.Message);
            }

            return taskInfo;
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

                    if (!sqlDataReader.Read())
                    {
                        return null;
                    }

                    var taskInfoTable = VLatest.TaskInfo;
                    _ = sqlDataReader.Read(taskInfoTable.TaskId, 0);
                    string queueId = sqlDataReader.Read(taskInfoTable.QueueId, 1);
                    short status = sqlDataReader.Read(taskInfoTable.Status, 2);
                    short taskTypeId = sqlDataReader.Read(taskInfoTable.TaskTypeId, 3);
                    string taskRunId = sqlDataReader.Read(taskInfoTable.RunId, 4);
                    bool isCanceled = sqlDataReader.Read(taskInfoTable.IsCanceled, 5);
                    short retryCount = sqlDataReader.Read(taskInfoTable.RetryCount, 6);
                    short maxRetryCount = sqlDataReader.Read(taskInfoTable.MaxRetryCount, 7);
                    DateTime? heartbeatDateTime = sqlDataReader.Read(taskInfoTable.HeartbeatDateTime, 8);
                    string inputData = sqlDataReader.Read(taskInfoTable.InputData, 9);
                    string taskContext = sqlDataReader.Read(taskInfoTable.TaskContext, 10);
                    string result = sqlDataReader.Read(taskInfoTable.Result, 11);

                    return new TaskInfo()
                    {
                        TaskId = taskId,
                        QueueId = queueId,
                        Status = (TaskStatus)status,
                        TaskTypeId = taskTypeId,
                        RunId = taskRunId,
                        IsCanceled = isCanceled,
                        RetryCount = retryCount,
                        MaxRetryCount = maxRetryCount,
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

        public async Task<TaskInfo> ResetAsync(string taskId, TaskResultData taskResultData, string runId, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                try
                {
                    VLatest.ResetTask.PopulateCommand(sqlCommandWrapper, taskId, runId, JsonConvert.SerializeObject(taskResultData));
                    SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                    if (!sqlDataReader.Read())
                    {
                        return null;
                    }

                    var taskInfoTable = VLatest.TaskInfo;
                    _ = sqlDataReader.Read(taskInfoTable.TaskId, 0);
                    string queueId = sqlDataReader.Read(taskInfoTable.QueueId, 1);
                    short status = sqlDataReader.Read(taskInfoTable.Status, 2);
                    short taskTypeId = sqlDataReader.Read(taskInfoTable.TaskTypeId, 3);
                    string taskRunId = sqlDataReader.Read(taskInfoTable.RunId, 4);
                    bool isCanceled = sqlDataReader.Read(taskInfoTable.IsCanceled, 5);
                    short retryCount = sqlDataReader.Read(taskInfoTable.RetryCount, 6);
                    short maxRetryCount = sqlDataReader.Read(taskInfoTable.MaxRetryCount, 7);
                    DateTime? heartbeatDateTime = sqlDataReader.Read(taskInfoTable.HeartbeatDateTime, 8);
                    string inputData = sqlDataReader.Read(taskInfoTable.InputData, 9);
                    string taskContext = sqlDataReader.Read(taskInfoTable.TaskContext, 10);
                    string result = sqlDataReader.Read(taskInfoTable.Result, 11);

                    TaskStatus taskStatus = (TaskStatus)status;
                    return taskStatus == TaskStatus.Completed
                        ? throw new TaskAlreadyCompletedException("Task already completed or reach max retry count.")
                        : new TaskInfo()
                        {
                            TaskId = taskId,
                            QueueId = queueId,
                            Status = taskStatus,
                            TaskTypeId = taskTypeId,
                            RunId = taskRunId,
                            IsCanceled = isCanceled,
                            RetryCount = retryCount,
                            MaxRetryCount = maxRetryCount,
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
