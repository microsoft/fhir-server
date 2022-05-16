// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.TaskManagement;
using NSubstitute;
using Xunit;
using TaskStatus = Microsoft.Health.TaskManagement.TaskStatus;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Use different queue type for integration test to avoid conflict
    /// </summary>
    internal enum TestQueueType : byte
    {
        GivenNewTasks_WhenEnqueueTasks_ThenCreatedTasksShouldBeReturned = 16,
        GivenNewTasksWithSameQueueType_WhenEnqueueWithForceOneActiveJobGroup_ThenSecondTaskShouldNotBeEnqueued,
        GivenTasksWithSameDefinition_WhenEnqueue_ThenOnlyOneTaskShouldBeEnqueued,
        GivenTasksWithSameDefinition_WhenEnqueueWithGroupId_ThenGroupIdShouldBeCorrect,
        GivenTasksEnqueue_WhenDequeue_ThenAllTasksShouldBeReturened,
        GivenTaskKeepPutHeartbeat_WhenDequeue_ThenTaskShouldNotBeReturened,
        GivenTaskKeepPutHeartbeatWithResult_WhenDequeue_ThenTaskWithResultShouldNotBeReturened,
        GivenTaskNotHeartbeat_WhenDequeue_ThenTaskShouldBeReturenedAgain,
        GivenGroupTasks_WhenCompleteTask_ThenTasksShouldBeCompleted,
        GivenGroupTasks_WhenCancelTasksByGroupId_ThenAllTasksShouldBeCancelled,
        GivenGroupTasks_WhenCancelTasksById_ThenOnlySingleTaskShouldBeCancelled,
        GivenGroupTasks_WhenOneTaskFailedAndRequestCancellation_ThenAllTasksShouldBeCancelled,
    }

    public class SqlQueueClientTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private SqlServerFhirStorageTestsFixture _fixture;
        private SchemaInformation _schemaInformation;
        private ILogger<SqlQueueClient> _logger = Substitute.For<ILogger<SqlQueueClient>>();

        public SqlQueueClientTests(SqlServerFhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            _schemaInformation.Current = SchemaVersionConstants.Max;
        }

        [Fact]
        public async Task GivenNewTasks_WhenEnqueueTasks_ThenCreatedTasksShouldBeReturned()
        {
            byte queueType = (byte)TestQueueType.GivenNewTasks_WhenEnqueueTasks_ThenCreatedTasksShouldBeReturned;

            SqlQueueClient sqlQueueClient = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, _logger);
            string[] definitions = new string[] { "task1", "task2" };
            IEnumerable<TaskInfo> taskInfos = await sqlQueueClient.EnqueueAsync(queueType, definitions, null, false, false, CancellationToken.None);

            Assert.Equal(2, taskInfos.Count());
            Assert.Equal(1, taskInfos.Last().Id - taskInfos.First().Id);
            Assert.Equal(TaskStatus.Created, taskInfos.First().Status);
            Assert.Null(taskInfos.First().StartDate);
            Assert.Null(taskInfos.First().EndDate);
            Assert.Equal(taskInfos.Last().GroupId, taskInfos.First().GroupId);

            TaskInfo taskinfo = await sqlQueueClient.GetTaskByIdAsync(queueType, taskInfos.First().Id, true, CancellationToken.None);
            Assert.Contains(taskinfo.Definition, definitions);

            taskinfo = await sqlQueueClient.GetTaskByIdAsync(queueType, taskInfos.Last().Id, true, CancellationToken.None);
            Assert.Contains(taskinfo.Definition, definitions);
        }

        [Fact]
        public async Task GivenNewTasksWithSameQueueType_WhenEnqueueWithForceOneActiveJobGroup_ThenSecondTaskShouldNotBeEnqueued()
        {
            byte queueType = (byte)TestQueueType.GivenNewTasksWithSameQueueType_WhenEnqueueWithForceOneActiveJobGroup_ThenSecondTaskShouldNotBeEnqueued;

            SqlQueueClient sqlQueueClient = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, _logger);
            IEnumerable<TaskInfo> taskInfos = await sqlQueueClient.EnqueueAsync(queueType, new string[] { "task1" }, null, true, false, CancellationToken.None);
            await Assert.ThrowsAsync<TaskConflictException>(async () => await sqlQueueClient.EnqueueAsync(queueType, new string[] { "task2" }, null, true, false, CancellationToken.None));
        }

        [Fact]
        public async Task GivenTasksWithSameDefinition_WhenEnqueue_ThenOnlyOneTaskShouldBeEnqueued()
        {
            byte queueType = (byte)TestQueueType.GivenTasksWithSameDefinition_WhenEnqueue_ThenOnlyOneTaskShouldBeEnqueued;

            SqlQueueClient sqlQueueClient = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, _logger);
            IEnumerable<TaskInfo> taskInfos = await sqlQueueClient.EnqueueAsync(queueType, new string[] { "task1"}, null, false, false, CancellationToken.None);
            Assert.Single(taskInfos);
            long taskId = taskInfos.First().Id;
            taskInfos = await sqlQueueClient.EnqueueAsync(queueType, new string[] { "task1"}, null, false, false, CancellationToken.None);
            Assert.Equal(taskId, taskInfos.First().Id);
        }

        [Fact]
        public async Task GivenTasksWithSameDefinition_WhenEnqueueWithGroupId_ThenGroupIdShouldBeCorrect()
        {
            byte queueType = (byte)TestQueueType.GivenTasksWithSameDefinition_WhenEnqueueWithGroupId_ThenGroupIdShouldBeCorrect;

            long groupId = new Random().Next(int.MinValue, int.MaxValue);
            SqlQueueClient sqlQueueClient = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, _logger);
            IEnumerable<TaskInfo> taskInfos = await sqlQueueClient.EnqueueAsync(queueType, new string[] { "task1", "task2" }, groupId, false, false, CancellationToken.None);
            Assert.Equal(2, taskInfos.Count());
            Assert.Equal(groupId, taskInfos.First().GroupId);
            Assert.Equal(groupId, taskInfos.Last().GroupId);
            taskInfos = await sqlQueueClient.EnqueueAsync(queueType, new string[] { "task3", "task4"}, groupId, false, false, CancellationToken.None);
            Assert.Equal(2, taskInfos.Count());
            Assert.Equal(groupId, taskInfos.First().GroupId);
            Assert.Equal(groupId, taskInfos.Last().GroupId);
        }

        [Fact]
        public async Task GivenTasksEnqueue_WhenDequeue_ThenAllTasksShouldBeReturened()
        {
            byte queueType = (byte)TestQueueType.GivenTasksEnqueue_WhenDequeue_ThenAllTasksShouldBeReturened;

            SqlQueueClient sqlQueueClient = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, _logger);
            await sqlQueueClient.EnqueueAsync(queueType, new string[] { "task1" }, null, false, false, CancellationToken.None);
            await sqlQueueClient.EnqueueAsync(queueType, new string[] { "task2" }, null, false, false, CancellationToken.None);

            List<string> definitions = new List<string>();
            TaskInfo taskInfo1 = await sqlQueueClient.DequeueAsync(queueType, 0, "test-worker", 10, CancellationToken.None);
            definitions.Add(taskInfo1.Definition);
            TaskInfo taskInfo2 = await sqlQueueClient.DequeueAsync(queueType, 0, "test-worker", 10, CancellationToken.None);
            definitions.Add(taskInfo2.Definition);
            Assert.Null(await sqlQueueClient.DequeueAsync(queueType, 0, "test-worker", 10, CancellationToken.None));

            Assert.Contains("task1", definitions);
            Assert.Contains("task2", definitions);
        }

        [Fact]
        public async Task GivenTaskKeepPutHeartbeat_WhenDequeue_ThenTaskShouldNotBeReturened()
        {
            byte queueType = (byte)TestQueueType.GivenTaskKeepPutHeartbeat_WhenDequeue_ThenTaskShouldNotBeReturened;

            SqlQueueClient sqlQueueClient = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, _logger);
            await sqlQueueClient.EnqueueAsync(queueType, new string[] { "task1" }, null, false, false, CancellationToken.None);

            TaskInfo taskInfo1 = await sqlQueueClient.DequeueAsync(queueType, 0, "test-worker", 1, CancellationToken.None);
            taskInfo1.QueueType = queueType;
            await Task.Delay(TimeSpan.FromSeconds(2));
            TaskInfo taskInfo2 = await sqlQueueClient.KeepAliveTaskAsync(taskInfo1, CancellationToken.None);
            Assert.Equal(taskInfo1.Id, taskInfo2.Id);
            Assert.Equal(taskInfo1.Version, taskInfo2.Version);
            Assert.Null(await sqlQueueClient.DequeueAsync(queueType, 0, "test-worker", 1, CancellationToken.None));
        }

        [Fact]
        public async Task GivenTaskKeepPutHeartbeatWithResult_WhenDequeue_ThenTaskWithResultShouldNotBeReturened()
        {
            byte queueType = (byte)TestQueueType.GivenTaskKeepPutHeartbeatWithResult_WhenDequeue_ThenTaskWithResultShouldNotBeReturened;

            SqlQueueClient sqlQueueClient = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, _logger);
            await sqlQueueClient.EnqueueAsync(queueType, new string[] { "task1" }, null, false, false, CancellationToken.None);

            TaskInfo taskInfo1 = await sqlQueueClient.DequeueAsync(queueType, 0, "test-worker", 1, CancellationToken.None);
            taskInfo1.QueueType = queueType;
            taskInfo1.Result = "current-result";
            await sqlQueueClient.KeepAliveTaskAsync(taskInfo1, CancellationToken.None);
            await Task.Delay(TimeSpan.FromSeconds(1));
            TaskInfo taskInfo2 = await sqlQueueClient.DequeueAsync(queueType, 0, "test-worker", 0, CancellationToken.None);
            Assert.Equal(taskInfo1.Result, taskInfo2.Result);
        }

        [Fact]
        public async Task GivenTaskNotHeartbeat_WhenDequeue_ThenTaskShouldBeReturenedAgain()
        {
            byte queueType = (byte)TestQueueType.GivenTaskNotHeartbeat_WhenDequeue_ThenTaskShouldBeReturenedAgain;

            SqlQueueClient sqlQueueClient = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, _logger);
            await sqlQueueClient.EnqueueAsync(queueType, new string[] { "task1" }, null, false, false, CancellationToken.None);

            TaskInfo taskInfo1 = await sqlQueueClient.DequeueAsync(queueType, 0, "test-worker", 0, CancellationToken.None);
            await Task.Delay(TimeSpan.FromSeconds(1));
            TaskInfo taskInfo2 = await sqlQueueClient.DequeueAsync(queueType, 0, "test-worker", 0, CancellationToken.None);
            Assert.Null(await sqlQueueClient.DequeueAsync(queueType, 0, "test-worker", 10, CancellationToken.None));

            Assert.Equal(taskInfo1.Id, taskInfo2.Id);
            Assert.True(taskInfo1.Version < taskInfo2.Version);
        }

        [Fact]
        public async Task GivenGroupTasks_WhenCompleteTask_ThenTasksShouldBeCompleted()
        {
            byte queueType = (byte)TestQueueType.GivenGroupTasks_WhenCompleteTask_ThenTasksShouldBeCompleted;

            SqlQueueClient sqlQueueClient = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, _logger);
            await sqlQueueClient.EnqueueAsync(queueType, new string[] { "task1", "task2" }, null, false, false, CancellationToken.None);

            TaskInfo taskInfo1 = await sqlQueueClient.DequeueAsync(queueType, 0, "test-worker", 0, CancellationToken.None);
            TaskInfo taskInfo2 = await sqlQueueClient.DequeueAsync(queueType, 0, "test-worker", 10, CancellationToken.None);

            Assert.Equal(TaskStatus.Running, taskInfo1.Status);
            taskInfo1.Status = TaskStatus.Failed;
            taskInfo1.Result = "Failed for cancellation";
            await sqlQueueClient.CompleteTaskAsync(taskInfo1, false, CancellationToken.None);
            TaskInfo taskInfo = await sqlQueueClient.GetTaskByIdAsync(queueType, taskInfo1.Id, false, CancellationToken.None);
            Assert.Equal(TaskStatus.Failed, taskInfo.Status);
            Assert.Equal(taskInfo1.Result, taskInfo.Result);

            taskInfo2.Status = TaskStatus.Completed;
            taskInfo2.Result = "Completed";
            await sqlQueueClient.CompleteTaskAsync(taskInfo2, false, CancellationToken.None);
            taskInfo = await sqlQueueClient.GetTaskByIdAsync(queueType, taskInfo2.Id, false, CancellationToken.None);
            Assert.Equal(TaskStatus.Completed, taskInfo.Status);
            Assert.Equal(taskInfo2.Result, taskInfo.Result);
        }

        [Fact]
        public async Task GivenGroupTasks_WhenCancelTasksByGroupId_ThenAllTasksShouldBeCancelled()
        {
            byte queueType = (byte)TestQueueType.GivenGroupTasks_WhenCancelTasksByGroupId_ThenAllTasksShouldBeCancelled;

            SqlQueueClient sqlQueueClient = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, _logger);
            await sqlQueueClient.EnqueueAsync(queueType, new string[] { "task1", "task2", "task3" }, null, false, false, CancellationToken.None);

            TaskInfo taskInfo1 = await sqlQueueClient.DequeueAsync(queueType, 0, "test-worker", 0, CancellationToken.None);
            TaskInfo taskInfo2 = await sqlQueueClient.DequeueAsync(queueType, 0, "test-worker", 0, CancellationToken.None);

            await sqlQueueClient.CancelTaskByGroupIdAsync(queueType, taskInfo1.GroupId, CancellationToken.None);
            Assert.True((await sqlQueueClient.GetTaskByGroupIdAsync(queueType, taskInfo1.GroupId, false, CancellationToken.None)).All(t => t.Status == TaskStatus.Cancelled || (t.Status == TaskStatus.Running && t.CancelRequested)));

            taskInfo1.Status = TaskStatus.Failed;
            taskInfo1.Result = "Failed for cancellation";
            await sqlQueueClient.CompleteTaskAsync(taskInfo1, false, CancellationToken.None);
            TaskInfo taskInfo = await sqlQueueClient.GetTaskByIdAsync(queueType, taskInfo1.Id, false, CancellationToken.None);
            Assert.Equal(TaskStatus.Failed, taskInfo.Status);
            Assert.Equal(taskInfo1.Result, taskInfo.Result);

            taskInfo2.Status = TaskStatus.Completed;
            taskInfo2.Result = "Completed";
            await sqlQueueClient.CompleteTaskAsync(taskInfo2, false, CancellationToken.None);
            taskInfo = await sqlQueueClient.GetTaskByIdAsync(queueType, taskInfo2.Id, false, CancellationToken.None);
            Assert.Equal(TaskStatus.Completed, taskInfo.Status);
            Assert.Equal(taskInfo2.Result, taskInfo.Result);
        }

        [Fact]
        public async Task GivenGroupTasks_WhenCancelTasksById_ThenOnlySingleTaskShouldBeCancelled()
        {
            byte queueType = (byte)TestQueueType.GivenGroupTasks_WhenCancelTasksById_ThenOnlySingleTaskShouldBeCancelled;

            SqlQueueClient sqlQueueClient = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, _logger);
            await sqlQueueClient.EnqueueAsync(queueType, new string[] { "task1", "task2", "task3" }, null, false, false, CancellationToken.None);

            TaskInfo taskInfo1 = await sqlQueueClient.DequeueAsync(queueType, 0, "test-worker", 0, CancellationToken.None);
            TaskInfo taskInfo2 = await sqlQueueClient.DequeueAsync(queueType, 0, "test-worker", 0, CancellationToken.None);

            await sqlQueueClient.CancelTaskByIdAsync(queueType, taskInfo1.Id, CancellationToken.None);
            Assert.True((await sqlQueueClient.GetTaskByIdAsync(queueType, taskInfo1.Id, false, CancellationToken.None)).CancelRequested);
            Assert.False((await sqlQueueClient.GetTaskByIdAsync(queueType, taskInfo2.Id, false, CancellationToken.None)).CancelRequested);
            Assert.NotNull(await sqlQueueClient.DequeueAsync(queueType, 0, "test-worker", 0, CancellationToken.None));
        }

        [Fact]
        public async Task GivenGroupTasks_WhenOneTaskFailedAndRequestCancellation_ThenAllTasksShouldBeCancelled()
        {
            byte queueType = (byte)TestQueueType.GivenGroupTasks_WhenOneTaskFailedAndRequestCancellation_ThenAllTasksShouldBeCancelled;

            SqlQueueClient sqlQueueClient = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, _logger);
            await sqlQueueClient.EnqueueAsync(queueType, new string[] { "task1", "task2", "task3" }, null, false, false, CancellationToken.None);

            TaskInfo taskInfo1 = await sqlQueueClient.DequeueAsync(queueType, 0, "test-worker", 0, CancellationToken.None);
            taskInfo1.Status = TaskStatus.Failed;
            taskInfo1.Result = "Failed for critical error";

            await sqlQueueClient.CompleteTaskAsync(taskInfo1, true, CancellationToken.None);
            Assert.True((await sqlQueueClient.GetTaskByGroupIdAsync(queueType, taskInfo1.GroupId, false, CancellationToken.None)).All(t => t.Status is (TaskStatus?)TaskStatus.Cancelled or (TaskStatus?)TaskStatus.Failed));
        }
    }
}
