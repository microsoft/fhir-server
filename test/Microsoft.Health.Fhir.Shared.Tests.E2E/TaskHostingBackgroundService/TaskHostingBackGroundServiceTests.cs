// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Threading.Tasks;
using Castle.Core.Internal;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.TaskManagement;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Newtonsoft.Json;
using Xunit;
using TaskStatus = Microsoft.Health.Fhir.TaskManagement.TaskStatus;

namespace Microsoft.Health.Fhir.Shared.Tests.E2E.TaskHostingBackgroundService
{
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class TaskHostingBackGroundServiceTests : IClassFixture<TaskHostingTestFixture>
    {
        private const string MockTaskId = "mockTask";

        private readonly bool _isUsingInProcTestServer;
        private readonly string _connectionString;

        public TaskHostingBackGroundServiceTests(TaskHostingTestFixture fixture)
        {
            _connectionString = fixture.ConnectionString;
            _isUsingInProcTestServer = fixture.IsUsingInProcTestServer;
        }

        [Theory(Skip = "need local environment for tests.")]
        [InlineData(MockTaskType.Success, TaskResult.Success, "0")]
        [InlineData(MockTaskType.Fail, TaskResult.Fail, "3")]
        public async Task GivenDifferentTypeTask_WhenInsertingTaskInToDatabase_ThenTaskResultShouldBeMatch(MockTaskType taskType, TaskResult expected, string context)
        {
            if (!_isUsingInProcTestServer)
            {
                // This test only works with the in-proc server with a customized task factory.
                return;
            }

            var taskId = $"{MockTaskId}_{taskType}";
            WriteTaskInfoToSql(taskId, taskType);

            var taskInfo = new TaskInfo { Status = TaskStatus.Created };
            var end = DateTimeOffset.UtcNow.Add(TimeSpan.FromMinutes(3));
            while (DateTimeOffset.UtcNow < end && taskInfo.Status != TaskStatus.Completed)
            {
                await Task.Delay(3000);
                taskInfo = GetTaskInfoFromSql(taskId);
            }

            Assert.Equal(TaskStatus.Completed, taskInfo.Status);
            TaskResultData result = JsonConvert.DeserializeObject<TaskResultData>(taskInfo.Result);
            Assert.Equal(expected, result.Result);

            // check that context updater should work
            Assert.Equal(context, taskInfo.Context);
        }

        [Theory(Skip = "need local environment for tests.")]
        [InlineData(3)]
        public async Task GivenThreeTasks_WhenCancelAllTasks_ThenAllTasksShouldBeCancelled(int taskCount)
        {
            if (!_isUsingInProcTestServer)
            {
                // This test only works with the in-proc server with a customized task factory.
                return;
            }

            for (int i = 0; i < taskCount; i++)
            {
                var taskId = $"{MockTaskId}_CancelTest_{i}";
                WriteTaskInfoToSql(taskId, MockTaskType.Loop);
            }

            // make sure all tasks are running
            var runningTaskCount = 0;
            var end = DateTimeOffset.UtcNow.Add(TimeSpan.FromMinutes(2));
            while (DateTimeOffset.UtcNow < end && runningTaskCount < taskCount)
            {
                await Task.Delay(5000);

                runningTaskCount = 0;
                for (int i = 0; i < taskCount; i++)
                {
                    var taskInfo = GetTaskInfoFromSql($"{MockTaskId}_CancelTest_{i}");
                    if (taskInfo.Status == TaskStatus.Running)
                    {
                        runningTaskCount++;
                    }
                }
            }

            Assert.Equal(taskCount, runningTaskCount);

            // cancel all tasks
            for (int i = 0; i < taskCount; i++)
            {
                var taskId = $"{MockTaskId}_CancelTest_{i}";
                CancelATask(taskId);
            }

            var canceledTask = 0;
            var taskInfos = new TaskInfo[taskCount];
            end = DateTimeOffset.UtcNow.Add(TimeSpan.FromMinutes(2));
            while (DateTimeOffset.UtcNow < end && canceledTask < taskCount)
            {
                await Task.Delay(5000);

                canceledTask = 0;
                for (int i = 0; i < taskCount; i++)
                {
                    var taskInfo = GetTaskInfoFromSql($"{MockTaskId}_CancelTest_{i}");
                    if (taskInfo.IsCanceled && taskInfo.Status == TaskStatus.Completed)
                    {
                        canceledTask++;
                    }

                    taskInfos[i] = taskInfo;
                }
            }

            Assert.Equal(taskCount, canceledTask);
            for (int i = 0; i < taskCount; i++)
            {
                TaskResultData result = JsonConvert.DeserializeObject<TaskResultData>(taskInfos[i].Result);
                Assert.Equal(TaskResult.Canceled, result.Result);
            }
        }

        [Theory(Skip = "need local environment for tests.")]
        [InlineData(5)]
        public async Task GivenMultipleTasks_WhenInsertingTasksInToDatabase_ThenRunningTasksNumberShouldBeSameAsTaskCount(int taskCount)
        {
            if (!_isUsingInProcTestServer)
            {
                // This test only works with the in-proc server with a customized task factory.
                return;
            }

            for (int i = 0; i < taskCount + 1; i++)
            {
                WriteTaskInfoToSql($"{MockTaskId}_ConcurrentTest_{i}", MockTaskType.Loop);
            }

            int runningTaskCount = 0;
            var taskInfos = new TaskInfo[taskCount + 5];
            var canceledRunningTaskId = string.Empty;

            for (int loop = 0; loop < 2; loop++)
            {
                var end = DateTimeOffset.UtcNow.Add(TimeSpan.FromMinutes(2));
                while (DateTimeOffset.UtcNow < end && runningTaskCount < taskCount)
                {
                    await Task.Delay(5000);

                    runningTaskCount = 0;
                    for (int i = 0; i < taskCount + 1; i++)
                    {
                        taskInfos[i] = GetTaskInfoFromSql($"{MockTaskId}_ConcurrentTest_{i}");
                        if (taskInfos[i].Status == TaskStatus.Running && !string.Equals(taskInfos[i].TaskId, canceledRunningTaskId))
                        {
                            runningTaskCount++;
                        }
                    }
                }

                Assert.Equal(taskCount, runningTaskCount);

                if (loop == 0)
                {
                    // cancel one task
                    canceledRunningTaskId = taskInfos.Find(taskInfo => (taskInfo.Status == TaskStatus.Running)).TaskId;
                    CancelATask(canceledRunningTaskId);
                    runningTaskCount = 0;
                }
            }

            for (int i = 0; i < taskCount + 1; i++)
            {
                CancelATask($"{MockTaskId}_ConcurrentTest_{i}");
            }
        }

        private void WriteTaskInfoToSql(string taskId, MockTaskType taskType = MockTaskType.Success)
        {
            TaskInfo taskInfo = CreateDefaultTaskInfo(taskId, taskType);
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                SqlCommand cmd = new SqlCommand("dbo.CreateTask", conn);
                cmd.CommandType = CommandType.StoredProcedure;

                cmd.Parameters.Add(new SqlParameter("@taskId", taskInfo.TaskId));
                cmd.Parameters.Add(new SqlParameter("@queueId", taskInfo.QueueId));
                cmd.Parameters.Add(new SqlParameter("@taskTypeId", taskInfo.TaskTypeId));
                cmd.Parameters.Add(new SqlParameter("@inputData", taskInfo.InputData));

                cmd.ExecuteNonQuery();
            }
        }

        private TaskInfo GetTaskInfoFromSql(string taskId)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                TaskInfo taskInfo = new TaskInfo();
                conn.Open();
                SqlCommand cmd = new SqlCommand("dbo.GetTaskDetails", conn);
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.Add(new SqlParameter("@taskId", taskId));

                var reader = cmd.ExecuteReader();
                if (!reader.Read())
                {
                    throw new Exception($"Can't get task {taskId}");
                }

                taskInfo.TaskId = reader.GetString(reader.GetOrdinal("TaskId"));
                int ordinal = reader.GetOrdinal("Result");
                taskInfo.Result = reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
                taskInfo.Status = (TaskStatus)reader.GetInt16(reader.GetOrdinal("Status"));
                ordinal = reader.GetOrdinal("TaskContext");
                taskInfo.Context = reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
                taskInfo.IsCanceled = reader.GetBoolean(reader.GetOrdinal("IsCanceled"));

                return taskInfo;
            }
        }

        private void CancelATask(string taskId)
        {
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                SqlCommand cmd = new SqlCommand("dbo.CancelTask", conn);
                cmd.CommandType = CommandType.StoredProcedure;

                cmd.Parameters.Add(new SqlParameter("@taskId", taskId));

                cmd.ExecuteNonQuery();
            }
        }

        private TaskInfo CreateDefaultTaskInfo(string taskId, MockTaskType taskTypeId)
        {
            return new TaskInfo
            {
                TaskId = taskId,
                QueueId = "0",
                TaskTypeId = (short)taskTypeId,
                InputData = string.Empty,
            };
        }
    }
}
