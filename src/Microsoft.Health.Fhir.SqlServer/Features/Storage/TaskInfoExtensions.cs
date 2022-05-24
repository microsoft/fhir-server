// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.TaskManagement;

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
                IsCanceled = isCanceled,
                RetryCount = retryCount,
                MaxRetryCount = maxRetryCount,
                HeartbeatDateTime = heartbeatDateTime,
                InputData = inputData,
                Context = taskContext,
                Result = result,
                ParentTaskId = parentTaskId,
            };
        }
    }
}
