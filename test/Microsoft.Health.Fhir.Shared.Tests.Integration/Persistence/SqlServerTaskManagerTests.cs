// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.TaskManagement;
using NSubstitute;
using Xunit;
using TaskStatus = Microsoft.Health.TaskManagement.TaskStatus;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    public class SqlServerTaskManagerTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private const short SqlServerTaskManagerTestsTypeId = 100;
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
            string inputData = "inputData";

            TaskInfo taskInfo = new TaskInfo()
            {
                TaskId = taskId,
                QueueId = queueId,
                TaskTypeId = SqlServerTaskManagerTestsTypeId,
                InputData = inputData,
            };

            SqlServerTaskManager sqlServerTaskManager = new SqlServerTaskManager(_fixture.SqlConnectionWrapperFactory, NullLogger<SqlServerTaskManager>.Instance);
            taskInfo = await sqlServerTaskManager.CreateTaskAsync(taskInfo, false, CancellationToken.None);

            Assert.Equal(queueId, taskInfo.QueueId);
            Assert.Equal(taskId, taskInfo.TaskId);
            Assert.Equal(SqlServerTaskManagerTestsTypeId, taskInfo.TaskTypeId);
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
            Assert.Equal(SqlServerTaskManagerTestsTypeId, taskInfo.TaskTypeId);
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
        public async Task GivenActiveTasks_WhenCreateWithSameTypeRunningTask_ThenConflictExceptionShouldBeThrow()
        {
            string queueId = Guid.NewGuid().ToString();
            string taskId1 = Guid.NewGuid().ToString();
            string taskId2 = Guid.NewGuid().ToString();
            string inputData = "inputData";

            TaskHostingConfiguration config = new TaskHostingConfiguration()
            {
                Enabled = true,
                QueueId = queueId,
                TaskHeartbeatTimeoutThresholdInSeconds = 60,
            };

            short conflictTestTypeId = 1000;

            IOptions<TaskHostingConfiguration> taskHostingConfig = Substitute.For<IOptions<TaskHostingConfiguration>>();
            taskHostingConfig.Value.Returns(config);
            SqlServerTaskManager sqlServerTaskManager = new SqlServerTaskManager(_fixture.SqlConnectionWrapperFactory, NullLogger<SqlServerTaskManager>.Instance);

            TaskInfo taskInfo1 = new TaskInfo()
            {
                TaskId = taskId1,
                QueueId = queueId,
                TaskTypeId = conflictTestTypeId,
                InputData = inputData,
            };

            TaskInfo taskInfo2 = new TaskInfo()
            {
                TaskId = taskId2,
                QueueId = queueId,
                TaskTypeId = conflictTestTypeId,
                InputData = inputData,
            };

            _ = await sqlServerTaskManager.CreateTaskAsync(taskInfo1, true, CancellationToken.None);
            await Assert.ThrowsAnyAsync<TaskConflictException>(() => sqlServerTaskManager.CreateTaskAsync(taskInfo2, true, CancellationToken.None));
        }

        [Fact]
        public async Task GivenASqlTaskManager_WhenCreate2TaskWithSameTaskId_ThenNewTaskShouldBeCreated()
        {
            string queueId = Guid.NewGuid().ToString();
            string taskId = Guid.NewGuid().ToString();
            string inputData = "inputData";

            TaskInfo taskInfo = new TaskInfo()
            {
                TaskId = taskId,
                QueueId = queueId,
                TaskTypeId = SqlServerTaskManagerTestsTypeId,
                InputData = inputData,
            };

            SqlServerTaskManager sqlServerTaskManager = new SqlServerTaskManager(_fixture.SqlConnectionWrapperFactory, NullLogger<SqlServerTaskManager>.Instance);
            taskInfo = await sqlServerTaskManager.CreateTaskAsync(taskInfo, false, CancellationToken.None);

            await Assert.ThrowsAsync<TaskConflictException>(async () => await sqlServerTaskManager.CreateTaskAsync(taskInfo, false, CancellationToken.None));
        }

        [Fact]
        public async Task GivenASqlTaskManager_WhenCancelTask_ThenTaskStatusShouldBeChanged()
        {
            string queueId = Guid.NewGuid().ToString();
            string taskId = Guid.NewGuid().ToString();
            string inputData = "inputData";

            TaskInfo taskInfo = new TaskInfo()
            {
                TaskId = taskId,
                QueueId = queueId,
                TaskTypeId = SqlServerTaskManagerTestsTypeId,
                InputData = inputData,
            };

            SqlServerTaskManager sqlServerTaskManager = new SqlServerTaskManager(_fixture.SqlConnectionWrapperFactory, NullLogger<SqlServerTaskManager>.Instance);
            _ = await sqlServerTaskManager.CreateTaskAsync(taskInfo, false, CancellationToken.None);

            TaskInfo canceledTask = await sqlServerTaskManager.CancelTaskAsync(taskId, CancellationToken.None);
            Assert.True(canceledTask.IsCanceled);
        }

        [Fact]
        public async Task GivenASqlTaskManager_WhenCreateTaskByTypeTwice_ConflictShouldBeReturned()
        {
            string queueId = Guid.NewGuid().ToString();
            string inputData = "inputData";
            short uniqueType = 1234;

            TaskInfo taskInfo = new TaskInfo()
            {
                TaskId = Guid.NewGuid().ToString(),
                QueueId = queueId,
                TaskTypeId = uniqueType,
                InputData = inputData,
            };

            SqlServerTaskManager sqlServerTaskManager = new SqlServerTaskManager(_fixture.SqlConnectionWrapperFactory, NullLogger<SqlServerTaskManager>.Instance);
            _ = await sqlServerTaskManager.CreateTaskAsync(taskInfo, true, CancellationToken.None);

            taskInfo = new TaskInfo()
            {
                TaskId = Guid.NewGuid().ToString(),
                QueueId = queueId,
                TaskTypeId = uniqueType,
                InputData = inputData,
            };

            await Assert.ThrowsAnyAsync<TaskConflictException>(async () => await sqlServerTaskManager.CreateTaskAsync(taskInfo, true, CancellationToken.None));
        }

        [Fact]
        public async Task GivenATaskCanceledButNotComplete_WhenCreateTaskBySameType_ConflictShouldBeReturned()
        {
            string queueId = Guid.NewGuid().ToString();
            string inputData = "inputData";
            short uniqueType = 4321;

            TaskInfo taskInfo = new TaskInfo()
            {
                TaskId = Guid.NewGuid().ToString(),
                QueueId = queueId,
                TaskTypeId = uniqueType,
                InputData = inputData,
            };

            SqlServerTaskManager sqlServerTaskManager = new SqlServerTaskManager(_fixture.SqlConnectionWrapperFactory, NullLogger<SqlServerTaskManager>.Instance);
            _ = await sqlServerTaskManager.CreateTaskAsync(taskInfo, true, CancellationToken.None);
            _ = await sqlServerTaskManager.CancelTaskAsync(taskInfo.TaskId, CancellationToken.None);

            taskInfo = new TaskInfo()
            {
                TaskId = Guid.NewGuid().ToString(),
                QueueId = queueId,
                TaskTypeId = uniqueType,
                InputData = inputData,
            };

            await Assert.ThrowsAnyAsync<TaskConflictException>(async () => await sqlServerTaskManager.CreateTaskAsync(taskInfo, true, CancellationToken.None));
        }
    }
}
