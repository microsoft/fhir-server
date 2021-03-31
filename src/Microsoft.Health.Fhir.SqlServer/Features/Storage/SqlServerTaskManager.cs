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
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.TaskManagement;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Storage;
using TaskStatus = Microsoft.Health.Fhir.Core.Features.TaskManagement.TaskStatus;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SqlServerTaskManager : ITaskManager
    {
        private SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private ILogger<SqlServerTaskManager> _logger;

        public SqlServerTaskManager(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            ILogger<SqlServerTaskManager> logger)
        {
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _logger = logger;
        }

        public async Task<TaskInfo> CreateTaskAsync(TaskInfo task, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                try
                {
                    VLatest.CreateTask.PopulateCommand(sqlCommandWrapper, task.TaskId, task.QueueId, task.TaskTypeId, task.InputData);
                    SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                    if (!sqlDataReader.Read())
                    {
                        return null;
                    }

                    var taskInfoTable = VLatest.TaskInfo;
                    (string taskId, string queueId, short status, short taskTypeId, string runId, bool isCanceled, short retryCount, DateTime? heartbeatDateTime, string inputData, string taskContext, string result) = sqlDataReader.ReadRow(
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
                        RunId = runId,
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
                    if (sqlEx.Number == SqlErrorCodes.Conflict)
                    {
                        throw new TaskConflictException(sqlEx.Message);
                    }

                    throw;
                }
            }
        }

        public async Task<TaskInfo> GetTaskAsync(string taskId, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.GetTaskDetails.PopulateCommand(sqlCommandWrapper, taskId);
                SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                if (!sqlDataReader.Read())
                {
                    return null;
                }

                var taskInfoTable = VLatest.TaskInfo;

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

                return new TaskInfo()
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
            }
        }

        public async Task<TaskInfo> CancelTaskAsync(string taskId, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.CancelTask.PopulateCommand(sqlCommandWrapper, taskId);
                SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                if (!sqlDataReader.Read())
                {
                    return null;
                }

                var taskInfoTable = VLatest.TaskInfo;

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

                return new TaskInfo()
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
            }
        }
    }
}
