// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Features.TaskManagement;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Tests.E2E.TaskHostingBackgroundService
{
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class TaskHostingBackGroundServiceTests : IClassFixture<TaskHostingTestFixture>
    {
        private const string MockTaskId = "mockTask";

        // private const string LocalConnectionString = "server=(local);Integrated Security=true";
        private const string LocalConnectionString = "Server=tcp:zhouzhou.database.windows.net,1433;Initial Catalog=zhou;Persist Security Info=False;User ID=zhou;Password=Zz+4121691;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";
        private readonly bool _isUsingInProcTestServer;
        private readonly string _connectionString;

        public TaskHostingBackGroundServiceTests(TaskHostingTestFixture fixture)
        {
            _connectionString = Environment.GetEnvironmentVariable("SqlServer:ConnectionString") ?? LocalConnectionString;
            _isUsingInProcTestServer = fixture.IsUsingInProcTestServer;
        }

        [Theory]
        [InlineData(0, TaskResult.Success)]
        [InlineData(1, TaskResult.Fail)]
        public async Task GivenDifferentTypeTask_WhenInsertingTaskInToDatabase_ThenTaskResultShouldBeMatch(short isFailure, TaskResult expected)
        {
            if (!_isUsingInProcTestServer)
            {
                // This test only works with the in-proc server with a customized task factory.
                return;
            }

            WriteTaskInfoToSql(isFailure);

            var taskInfo = new TaskInfo { Status = Fhir.Core.Features.TaskManagement.TaskStatus.Created };
            var end = DateTimeOffset.UtcNow.Add(TimeSpan.FromMinutes(2));
            while (DateTimeOffset.UtcNow < end && taskInfo.Status != Fhir.Core.Features.TaskManagement.TaskStatus.Completed)
            {
                await Task.Delay(3000);
                taskInfo = GetTaskInfoFromSql(isFailure);
            }

            Assert.Equal(Fhir.Core.Features.TaskManagement.TaskStatus.Completed, taskInfo.Status);
            var result = TaskResultData.ResloveTaskResultFromDbString(taskInfo.Result);
            Assert.Equal(expected, result.Result);
        }

        private void WriteTaskInfoToSql(short isFailure)
        {
            TaskInfo taskInfo = CreateDefaultTaskInfo((short)(isFailure + 100));
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

        private TaskInfo GetTaskInfoFromSql(short isFailure = 0)
        {
            var taskId = MockTaskId + (isFailure + 100);
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
                taskInfo.Status = (Fhir.Core.Features.TaskManagement.TaskStatus)reader.GetInt16(reader.GetOrdinal("Status"));

                return taskInfo;
            }
        }

        private TaskInfo CreateDefaultTaskInfo(short taskTypeId)
        {
            return new TaskInfo
            {
                TaskId = MockTaskId + taskTypeId,
                QueueId = "0",
                TaskTypeId = taskTypeId,
                InputData = string.Empty,
            };
        }
    }
}
