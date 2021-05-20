// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.TaskManagement;
using NSubstitute;
using Xunit;
using TaskStatus = Microsoft.Health.TaskManagement.TaskStatus;

namespace Microsoft.Health.Fhir.Shared.Tests.Integration.Persistence
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
            taskInfo = await sqlServerTaskManager.CreateTaskAsync(taskInfo, CancellationToken.None);

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
        public async Task GivenActiveTasks_WhenGetActiveTasks_ThenActiveTasksShouldBeCreated()
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

            IOptions<TaskHostingConfiguration> taskHostingConfig = Substitute.For<IOptions<TaskHostingConfiguration>>();
            taskHostingConfig.Value.Returns(config);
            SqlServerTaskManager sqlServerTaskManager = new SqlServerTaskManager(_fixture.SqlConnectionWrapperFactory, NullLogger<SqlServerTaskManager>.Instance);
            SqlServerTaskConsumer sqlServerTaskConsumer = new SqlServerTaskConsumer(taskHostingConfig, _fixture.SqlConnectionWrapperFactory, NullLogger<SqlServerTaskConsumer>.Instance);

            TaskInfo taskInfo1 = new TaskInfo()
            {
                TaskId = taskId1,
                QueueId = queueId,
                TaskTypeId = SqlServerTaskManagerTestsTypeId,
                InputData = inputData,
            };

            TaskInfo taskInfo2 = new TaskInfo()
            {
                TaskId = taskId2,
                QueueId = queueId,
                TaskTypeId = SqlServerTaskManagerTestsTypeId,
                InputData = inputData,
            };

            _ = await sqlServerTaskManager.CreateTaskAsync(taskInfo1, CancellationToken.None);
            taskInfo2 = await sqlServerTaskManager.CreateTaskAsync(taskInfo2, CancellationToken.None);

            taskInfo1 = (await sqlServerTaskConsumer.GetNextMessagesAsync(1, 60, CancellationToken.None)).First();
            await sqlServerTaskConsumer.CompleteAsync(taskInfo1.TaskId, new TaskResultData(TaskResult.Success, "Result"), taskInfo1.RunId, CancellationToken.None);

            var activeTasks = (await sqlServerTaskManager.GetActiveTasksByTypeAsync(1, CancellationToken.None)).ToArray();
            activeTasks = activeTasks.Where(t => t.QueueId.Equals(queueId)).ToArray();
            Assert.Single(activeTasks);
            Assert.Equal(taskInfo2.TaskId, activeTasks.First().TaskId);
        }

        [Fact]
        public async Task GivenNoActiveTasks_WhenGetActiveTasks_ThenEmptyListShouldBeCreated()
        {
            string queueId = Guid.NewGuid().ToString();
            string taskId1 = Guid.NewGuid().ToString();
            string inputData = "inputData";

            TaskHostingConfiguration config = new TaskHostingConfiguration()
            {
                Enabled = true,
                QueueId = queueId,
                TaskHeartbeatTimeoutThresholdInSeconds = 60,
            };

            IOptions<TaskHostingConfiguration> taskHostingConfig = Substitute.For<IOptions<TaskHostingConfiguration>>();
            taskHostingConfig.Value.Returns(config);
            SqlServerTaskManager sqlServerTaskManager = new SqlServerTaskManager(_fixture.SqlConnectionWrapperFactory, NullLogger<SqlServerTaskManager>.Instance);
            SqlServerTaskConsumer sqlServerTaskConsumer = new SqlServerTaskConsumer(taskHostingConfig, _fixture.SqlConnectionWrapperFactory, NullLogger<SqlServerTaskConsumer>.Instance);

            TaskInfo taskInfo1 = new TaskInfo()
            {
                TaskId = taskId1,
                QueueId = queueId,
                TaskTypeId = SqlServerTaskManagerTestsTypeId,
                InputData = inputData,
            };

            _ = await sqlServerTaskManager.CreateTaskAsync(taskInfo1, CancellationToken.None);
            taskInfo1 = (await sqlServerTaskConsumer.GetNextMessagesAsync(1, 60, CancellationToken.None)).First();
            await sqlServerTaskConsumer.CompleteAsync(taskInfo1.TaskId, new TaskResultData(TaskResult.Success, "Result"), taskInfo1.RunId, CancellationToken.None);

            var activeTasks = (await sqlServerTaskManager.GetActiveTasksByTypeAsync(1, CancellationToken.None)).ToArray();
            activeTasks = activeTasks.Where(t => t.QueueId.Equals(queueId)).ToArray();
            Assert.Empty(activeTasks);
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
            taskInfo = await sqlServerTaskManager.CreateTaskAsync(taskInfo, CancellationToken.None);

            await Assert.ThrowsAsync<TaskConflictException>(async () => await sqlServerTaskManager.CreateTaskAsync(taskInfo, CancellationToken.None));
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
            _ = await sqlServerTaskManager.CreateTaskAsync(taskInfo, CancellationToken.None);

            TaskInfo canceledTask = await sqlServerTaskManager.CancelTaskAsync(taskId, CancellationToken.None);
            Assert.True(canceledTask.IsCanceled);
        }
    }
}
