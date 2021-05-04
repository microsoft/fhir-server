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
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.TaskManagement;
using TaskStatus = Microsoft.Health.TaskManagement.TaskStatus;

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
                    VLatest.CreateTask.PopulateCommand(sqlCommandWrapper, task.TaskId, task.QueueId, task.TaskTypeId, task.MaxRetryCount, task.InputData);
                    SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(cancellationToken);

                    if (!sqlDataReader.Read())
                    {
                        return null;
                    }

                    var taskInfoTable = VLatest.TaskInfo;

                    string taskId = sqlDataReader.Read(taskInfoTable.TaskId, 0);
                    string queueId = sqlDataReader.Read(taskInfoTable.QueueId, 1);
                    short status = sqlDataReader.Read(taskInfoTable.Status, 2);
                    short taskTypeId = sqlDataReader.Read(taskInfoTable.TaskTypeId, 3);
                    string taskRunId = sqlDataReader.Read(taskInfoTable.RunId, 4);
                    bool isCanceled = sqlDataReader.Read(taskInfoTable.IsCanceled, 5);
                    short retryCount = sqlDataReader.Read(taskInfoTable.RetryCount, 6);
                    short maxRetryCount = sqlDataReader.Read(taskInfoTable.MaxRetryCount, 7);
                    DateTime? heartbeatDateTime = sqlDataReader.Read(taskInfoTable.HeartbeatDateTime, 8);
                    string inputData = sqlDataReader.Read(taskInfoTable.InputData, 9);

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

                string id = sqlDataReader.Read(taskInfoTable.TaskId, 0);
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

                string id = sqlDataReader.Read(taskInfoTable.TaskId, 0);
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
    }
}
