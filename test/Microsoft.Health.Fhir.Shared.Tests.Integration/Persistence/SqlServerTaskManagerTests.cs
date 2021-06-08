// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.TaskManagement;
using Xunit;
using TaskStatus = Microsoft.Health.TaskManagement.TaskStatus;

namespace Microsoft.Health.Fhir.Shared.Tests.Integration.Persistence
{
    public class SqlServerTaskManagerTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private SqlServerFhirStorageTestsFixture _fixture;

        public SqlServerTaskManagerTests(SqlServerFhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task GivenASqlTaskManager_WhenCreateTask_ThenNewTaskShouldBeCreated()
        {
            string queueId = Guid.NewGuid().ToString();
            string taskId = Guid.NewGuid().ToString();
            short typeId = 1;
            string inputData = "inputData";

            TaskInfo taskInfo = new TaskInfo()
            {
                TaskId = taskId,
                QueueId = queueId,
                TaskTypeId = typeId,
                InputData = inputData,
            };

            SqlServerTaskManager sqlServerTaskManager = new SqlServerTaskManager(_fixture.SqlConnectionWrapperFactory, NullLogger<SqlServerTaskManager>.Instance);
            taskInfo = await sqlServerTaskManager.CreateTaskAsync(taskInfo, CancellationToken.None);

            Assert.Equal(queueId, taskInfo.QueueId);
            Assert.Equal(taskId, taskInfo.TaskId);
            Assert.Equal(typeId, taskInfo.TaskTypeId);
            Assert.Equal(inputData, taskInfo.InputData);
            Assert.Equal(TaskStatus.Queued, taskInfo.Status);
            Assert.NotNull(taskInfo.HeartbeatDateTime);
            Assert.Null(taskInfo.RunId);
            Assert.False(taskInfo.IsCanceled);
            Assert.Equal(0, taskInfo.RetryCount);
            Assert.Null(taskInfo.Context);
            Assert.Null(taskInfo.Result);

            taskInfo = await sqlServerTaskManager.GetTaskAsync(taskId, CancellationToken.None);
            Assert.Equal(queueId, taskInfo.QueueId);
            Assert.Equal(taskId, taskInfo.TaskId);
            Assert.Equal(typeId, taskInfo.TaskTypeId);
            Assert.Equal(inputData, taskInfo.InputData);
            Assert.Equal(TaskStatus.Queued, taskInfo.Status);
            Assert.NotNull(taskInfo.HeartbeatDateTime);
            Assert.Null(taskInfo.RunId);
            Assert.False(taskInfo.IsCanceled);
            Assert.Equal(0, taskInfo.RetryCount);
            Assert.Null(taskInfo.Context);
            Assert.Null(taskInfo.Result);
        }

        [Fact]
        public async Task GivenASqlTaskManager_WhenCreate2TaskWithSameTaskId_ThenNewTaskShouldBeCreated()
        {
            string queueId = Guid.NewGuid().ToString();
            string taskId = Guid.NewGuid().ToString();
            short typeId = 1;
            string inputData = "inputData";

            TaskInfo taskInfo = new TaskInfo()
            {
                TaskId = taskId,
                QueueId = queueId,
                TaskTypeId = typeId,
                InputData = inputData,
            };

            SqlServerTaskManager sqlServerTaskManager = new SqlServerTaskManager(_fixture.SqlConnectionWrapperFactory, NullLogger<SqlServerTaskManager>.Instance);
            taskInfo = await sqlServerTaskManager.CreateTaskAsync(taskInfo, CancellationToken.None);

            await Assert.ThrowsAsync<TaskConflictException>(async () => await sqlServerTaskManager.CreateTaskAsync(taskInfo, CancellationToken.None));
        }

        [Fact]
        public async Task GivenASqlTaskManager_WhenCancelTask_ThenTaskStatusShouldBeChanged()
        {
            string queueId = Guid.NewGuid().ToString();
            string taskId = Guid.NewGuid().ToString();
            short typeId = 1;
            string inputData = "inputData";

            TaskInfo taskInfo = new TaskInfo()
            {
                TaskId = taskId,
                QueueId = queueId,
                TaskTypeId = typeId,
                InputData = inputData,
            };

            SqlServerTaskManager sqlServerTaskManager = new SqlServerTaskManager(_fixture.SqlConnectionWrapperFactory, NullLogger<SqlServerTaskManager>.Instance);
            _ = await sqlServerTaskManager.CreateTaskAsync(taskInfo, CancellationToken.None);

            TaskInfo canceledTask = await sqlServerTaskManager.CancelTaskAsync(taskId, CancellationToken.None);
            Assert.True(canceledTask.IsCanceled);
        }
    }
}
