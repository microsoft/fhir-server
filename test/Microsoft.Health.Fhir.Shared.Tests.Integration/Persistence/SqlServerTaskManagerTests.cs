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
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.TaskManagement;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    internal enum TestQueueType : byte
    {
        GivenASqlTaskManager_WhenEnqueue_ThenNewTaskShouldBeEnqueued,
    }

    public class SqlServerTaskManagerTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private const short SqlServerTaskManagerTestsTypeId = 100;
        private SqlServerFhirStorageTestsFixture _fixture;
        private SchemaInformation _schemaInformation;

        public SqlServerTaskManagerTests(SqlServerFhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            _schemaInformation.Current = SchemaVersionConstants.Max;
        }

        [Fact]
        public async Task GivenASqlTaskManager_WhenCreateTask_ThenNewTaskShouldBeCreated()
        {
            SqlServerTaskManager sqlServerTaskManager = new SqlServerTaskManager(_fixture.SqlConnectionWrapperFactory, _schemaInformation, NullLogger<SqlServerTaskManager>.Instance);
            var taskInfos = await sqlServerTaskManager.EnqueueAsync((byte)TestQueueType.GivenASqlTaskManager_WhenEnqueue_ThenNewTaskShouldBeEnqueued, new string[] { "task1" }, null, false, CancellationToken.None);

            taskInfos.ToString();
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
            SqlServerTaskManager sqlServerTaskManager = new SqlServerTaskManager(_fixture.SqlConnectionWrapperFactory, _schemaInformation, NullLogger<SqlServerTaskManager>.Instance);

            TaskInfo taskInfo1 = new TaskInfo()
            {
                TaskId = taskId1,
                QueueId = queueId,
                TaskTypeId = conflictTestTypeId,
                Definition = inputData,
            };

            TaskInfo taskInfo2 = new TaskInfo()
            {
                TaskId = taskId2,
                QueueId = queueId,
                TaskTypeId = conflictTestTypeId,
                Definition = inputData,
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
                Definition = inputData,
            };

            SqlServerTaskManager sqlServerTaskManager = new SqlServerTaskManager(_fixture.SqlConnectionWrapperFactory, _schemaInformation, NullLogger<SqlServerTaskManager>.Instance);
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
                Definition = inputData,
            };

            SqlServerTaskManager sqlServerTaskManager = new SqlServerTaskManager(_fixture.SqlConnectionWrapperFactory, _schemaInformation, NullLogger<SqlServerTaskManager>.Instance);
            _ = await sqlServerTaskManager.CreateTaskAsync(taskInfo, false, CancellationToken.None);

            TaskInfo canceledTask = await sqlServerTaskManager.CancelTaskAsync(taskId, CancellationToken.None);
            Assert.True(canceledTask.CancelRequested);
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
                Definition = inputData,
            };

            SqlServerTaskManager sqlServerTaskManager = new SqlServerTaskManager(_fixture.SqlConnectionWrapperFactory, _schemaInformation, NullLogger<SqlServerTaskManager>.Instance);
            _ = await sqlServerTaskManager.CreateTaskAsync(taskInfo, true, CancellationToken.None);

            taskInfo = new TaskInfo()
            {
                TaskId = Guid.NewGuid().ToString(),
                QueueId = queueId,
                TaskTypeId = uniqueType,
                Definition = inputData,
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
                Definition = inputData,
            };

            SqlServerTaskManager sqlServerTaskManager = new SqlServerTaskManager(_fixture.SqlConnectionWrapperFactory, _schemaInformation, NullLogger<SqlServerTaskManager>.Instance);
            _ = await sqlServerTaskManager.CreateTaskAsync(taskInfo, true, CancellationToken.None);
            _ = await sqlServerTaskManager.CancelTaskAsync(taskInfo.TaskId, CancellationToken.None);

            taskInfo = new TaskInfo()
            {
                TaskId = Guid.NewGuid().ToString(),
                QueueId = queueId,
                TaskTypeId = uniqueType,
                Definition = inputData,
            };

            await Assert.ThrowsAnyAsync<TaskConflictException>(async () => await sqlServerTaskManager.CreateTaskAsync(taskInfo, true, CancellationToken.None));
        }
    }
}
