// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public class SqlTaskConsumer : ITaskConsumer, IDisposable
    {
        private SqlConnection _connection;
        private string _queueId;

        public SqlTaskConsumer(string connectionString, string queueId)
        {
            _queueId = queueId;
            _connection = new SqlConnection(connectionString);
            _connection.Open();
        }

        public async Task<TaskInfo> CompleteAsync(TaskInfo task)
        {
            using SqlCommand completeTaskCommand = new SqlCommand("[dbo].[CompleteTask]", _connection);
            completeTaskCommand.CommandType = System.Data.CommandType.StoredProcedure;
            completeTaskCommand.Parameters.Add(new SqlParameter("@taskId", task.TaskId));
            completeTaskCommand.Parameters.Add(new SqlParameter("@taskContext", task.TaskContext));

            using SqlDataReader sqlDataReader = await completeTaskCommand.ExecuteReaderAsync();
            if (sqlDataReader.Read())
            {
                TaskInfo result = new TaskInfo();
                result.TaskId = sqlDataReader.GetString(0);
                result.GroupId = sqlDataReader.GetString(1);
                result.QueueId = sqlDataReader.GetString(2);
                result.Status = sqlDataReader.GetInt16(3);
                result.IsCanceled = sqlDataReader.GetBoolean(4);
                result.TaskTypeId = sqlDataReader.GetInt16(5);
                result.HeartbeatDateTime = sqlDataReader.GetDateTime(6);
                result.InputData = sqlDataReader.GetString(7);
                result.TaskContext = sqlDataReader.GetString(8);

                return result;
            }
            else
            {
                return null;
            }
        }

        public async Task<IReadOnlyCollection<TaskInfo>> GetNextMessagesAsync(int count)
        {
            using SqlCommand getNextTasksCommand = new SqlCommand("[dbo].[GetNextTask]", _connection);
            getNextTasksCommand.CommandType = System.Data.CommandType.StoredProcedure;
            getNextTasksCommand.Parameters.Add(new SqlParameter("@queueId", _queueId));
            getNextTasksCommand.Parameters.Add(new SqlParameter("@count", count));

            using SqlDataReader sqlDataReader = await getNextTasksCommand.ExecuteReaderAsync();
            List<TaskInfo> result = new List<TaskInfo>();
            while (sqlDataReader.Read())
            {
                TaskInfo taskInfo = new TaskInfo();
                taskInfo.TaskId = sqlDataReader.GetString(0);
                taskInfo.GroupId = sqlDataReader.GetString(1);
                taskInfo.QueueId = sqlDataReader.GetString(2);
                taskInfo.Status = sqlDataReader.GetInt16(3);
                taskInfo.IsCanceled = sqlDataReader.GetBoolean(4);
                taskInfo.TaskTypeId = sqlDataReader.GetInt16(5);
                taskInfo.HeartbeatDateTime = sqlDataReader.GetDateTime(6);
                taskInfo.InputData = sqlDataReader.GetString(7);
                taskInfo.TaskContext = sqlDataReader.GetString(8);

                result.Add(taskInfo);
            }

            return result;
        }

        public async Task<TaskInfo> UpdateContextAsync(TaskInfo task)
        {
            using SqlCommand updateTaskContextCommand = new SqlCommand("[dbo].[UpdateTaskContext]", _connection);
            updateTaskContextCommand.CommandType = System.Data.CommandType.StoredProcedure;
            updateTaskContextCommand.Parameters.Add(new SqlParameter("@taskId", task.TaskId));
            updateTaskContextCommand.Parameters.Add(new SqlParameter("@taskContext", task.TaskContext));

            using SqlDataReader sqlDataReader = await updateTaskContextCommand.ExecuteReaderAsync();
            if (sqlDataReader.Read())
            {
                TaskInfo result = new TaskInfo();
                result.TaskId = sqlDataReader.GetString(0);
                result.GroupId = sqlDataReader.GetString(1);
                result.QueueId = sqlDataReader.GetString(2);
                result.Status = sqlDataReader.GetInt16(3);
                result.IsCanceled = sqlDataReader.GetBoolean(4);
                result.TaskTypeId = sqlDataReader.GetInt16(5);
                result.HeartbeatDateTime = sqlDataReader.GetDateTime(6);
                result.InputData = sqlDataReader.GetString(7);
                result.TaskContext = sqlDataReader.GetString(8);

                return result;
            }
            else
            {
                return null;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _connection?.Close();
                _connection?.Dispose();
                _connection = null;
            }
        }
    }
}
