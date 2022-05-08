// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.TaskManagement;
using TaskStatus = Microsoft.Health.TaskManagement.TaskStatus;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public static class TaskInfoExtensions
    {
        public static TaskInfo ReadTaskInfo(this SqlDataReader sqlDataReader)
        {
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
            string taskContext = sqlDataReader.FieldCount > 10 ? sqlDataReader.Read(taskInfoTable.Result, 10) : null;
            string result = sqlDataReader.FieldCount > 11 ? sqlDataReader.Read(taskInfoTable.Result, 11) : null;
            string parentTaskId = sqlDataReader.FieldCount > 12 ? sqlDataReader.Read(taskInfoTable.ParentTaskId, 12) : null;

            return new TaskInfo()
            {
                TaskId = taskId,
                QueueId = queueId,
                Status = (TaskStatus)status,
                TaskTypeId = taskTypeId,
                RunId = taskRunId,
                CancelRequested = isCanceled,
                RetryCount = retryCount,
                MaxRetryCount = maxRetryCount,
                HeartbeatDateTime = heartbeatDateTime ?? DateTime.Now,
                Definition = inputData,
                Context = taskContext,
                Result = result,
                ParentTaskId = parentTaskId,
            };
        }

        public static async Task<TaskInfo> ReadTaskInfoAsync(this SqlDataReader sqlDataReader, CancellationToken cancellationToken)
        {
            if (await sqlDataReader.ReadAsync(cancellationToken))
            {
                return LoadTaskInfo(sqlDataReader);
            }

            return null;
        }

        public static async Task<IEnumerable<TaskInfo>> ReadTaskInfosAsync(this SqlDataReader sqlDataReader, CancellationToken cancellationToken)
        {
            List<TaskInfo> outcome = new List<TaskInfo>();
            while (await sqlDataReader.ReadAsync(cancellationToken))
            {
                TaskInfo taskInfo = LoadTaskInfo(sqlDataReader);
                outcome.Add(taskInfo);
            }

            return outcome;
        }

        private static TaskInfo LoadTaskInfo(SqlDataReader sqlDataReader)
        {
            var jobQueueTable = VLatest.JobQueue;
            long groupId = sqlDataReader.Read(jobQueueTable.GroupId, 0);
            long id = sqlDataReader.Read(jobQueueTable.JobId, 1);
            object definitionObj = sqlDataReader.GetValue(2);
            string definition = definitionObj is DBNull ? null : (string)definitionObj;
            long version = sqlDataReader.Read(jobQueueTable.Version, 3);
            TaskStatus status = (TaskStatus)sqlDataReader.Read(jobQueueTable.Status, 4);
            long priority = sqlDataReader.Read(jobQueueTable.Priority, 5);
            long? data = sqlDataReader.Read(jobQueueTable.Data, 6);
            string result = sqlDataReader.Read(jobQueueTable.Result, 7);
            DateTime createDate = sqlDataReader.Read(jobQueueTable.CreateDate, 8);
            object startDateObj = sqlDataReader.GetValue(9);
            DateTime? startDate = startDateObj is DBNull ? null : (DateTime)startDateObj;
            object endDateObj = sqlDataReader.GetValue(9);
            DateTime? endDate = endDateObj is DBNull ? null : (DateTime)endDateObj;
            DateTime heartbeatDate = sqlDataReader.Read(jobQueueTable.HeartbeatDate, 11);
            bool cancelRequested = sqlDataReader.Read(jobQueueTable.CancelRequested, 12);

            TaskInfo taskInfo = new TaskInfo()
            {
                Id = id,
                GroupId = groupId,
                Definition = definition,
                Version = version,
                Status = status,
                Priority = priority,
                Data = data,
                Result = result,
                CreateDate = createDate,
                StartDate = startDate,
                EndDate = endDate,
                HeartbeatDateTime = heartbeatDate,
                CancelRequested = cancelRequested,
            };
            return taskInfo;
        }
    }
}
