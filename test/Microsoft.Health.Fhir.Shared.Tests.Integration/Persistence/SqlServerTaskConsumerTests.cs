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
using Newtonsoft.Json;
using NSubstitute;
using Xunit;
using TaskStatus = Microsoft.Health.TaskManagement.TaskStatus;

namespace Microsoft.Health.Fhir.Shared.Tests.Integration.Persistence
{
    public class SqlServerTaskConsumerTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private SqlServerFhirStorageTestsFixture _fixture;

        public SqlServerTaskConsumerTests(SqlServerFhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task GivenListOfTasksInQueue_WhenGetNextTask_ThenAvailableTasksShouldBeReturned()
        {
            string queueId = Guid.NewGuid().ToString();
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

            for (int i = 0; i < 5; ++i)
            {
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

                _ = await sqlServerTaskManager.CreateTaskAsync(taskInfo, CancellationToken.None);
            }

            var result = (await sqlServerTaskConsumer.GetNextMessagesAsync(3, 60, CancellationToken.None)).ToList();
            Assert.Equal(3, result.Count());
        }

        [Fact]
        public async Task GivenACompletedTask_WhenGetNextTask_ThenNoTaskShouldBeReturned()
        {
            string queueId = Guid.NewGuid().ToString();
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

            _ = await sqlServerTaskManager.CreateTaskAsync(taskInfo, CancellationToken.None);

            taskInfo = (await sqlServerTaskConsumer.GetNextMessagesAsync(1, 60, CancellationToken.None)).First();
            TaskResultData result = new TaskResultData(TaskResult.Success, "Result");
            taskInfo = await sqlServerTaskConsumer.CompleteAsync(taskInfo.TaskId, result, taskInfo.RunId, CancellationToken.None);
            Assert.Equal(TaskStatus.Completed, taskInfo.Status);
            Assert.Equal(JsonConvert.SerializeObject(result), taskInfo.Result);

            taskInfo = (await sqlServerTaskConsumer.GetNextMessagesAsync(1, 60, CancellationToken.None)).FirstOrDefault();
            Assert.Null(taskInfo);
        }

        [Fact]
        public async Task GivenARunningTask_WhenGetNextTask_ThenNoTaskShouldBeReturned()
        {
            string queueId = Guid.NewGuid().ToString();
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

            _ = await sqlServerTaskManager.CreateTaskAsync(taskInfo, CancellationToken.None);

            _ = await sqlServerTaskConsumer.GetNextMessagesAsync(1, 60, CancellationToken.None);
            taskInfo = (await sqlServerTaskConsumer.GetNextMessagesAsync(1, 60, CancellationToken.None)).FirstOrDefault();
            Assert.Null(taskInfo);
        }

        [Fact]
        public async Task GivenARunningTaskTimeout_WhenGetNextTask_ThenTaskShouldBeReturned()
        {
            string queueId = Guid.NewGuid().ToString();
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

            _ = await sqlServerTaskManager.CreateTaskAsync(taskInfo, CancellationToken.None);

            _ = await sqlServerTaskConsumer.GetNextMessagesAsync(1, 60, CancellationToken.None);
            await Task.Delay(TimeSpan.FromSeconds(3));
            taskInfo = (await sqlServerTaskConsumer.GetNextMessagesAsync(1, 1, CancellationToken.None)).FirstOrDefault();
            Assert.NotNull(taskInfo);
        }

        [Fact]
        public async Task GivenARunningTask_WhenResetTask_ThenTaskShouldBeReturned()
        {
            string queueId = Guid.NewGuid().ToString();
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

            _ = await sqlServerTaskManager.CreateTaskAsync(taskInfo, CancellationToken.None);

            taskInfo = (await sqlServerTaskConsumer.GetNextMessagesAsync(1, 60, CancellationToken.None)).First();
            string firstRunId = taskInfo.RunId;
            TaskResultData result = new TaskResultData(TaskResult.Success, "Result");
            taskInfo = await sqlServerTaskConsumer.ResetAsync(taskInfo.TaskId, result, taskInfo.RunId, CancellationToken.None);
            Assert.Equal(1, taskInfo.RetryCount);

            taskInfo = (await sqlServerTaskConsumer.GetNextMessagesAsync(1, 60, CancellationToken.None)).First();
            Assert.NotNull(taskInfo);
            Assert.NotEqual(firstRunId, taskInfo.RunId);
        }

        [Fact]
        public async Task GivenARunningTask_WhenCompleteTask_ThenTaskStatusShouldBeChanged()
        {
            string queueId = Guid.NewGuid().ToString();
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

            _ = await sqlServerTaskManager.CreateTaskAsync(taskInfo, CancellationToken.None);

            taskInfo = (await sqlServerTaskConsumer.GetNextMessagesAsync(1, 60, CancellationToken.None)).First();
            TaskResultData result = new TaskResultData(TaskResult.Success, "Result");
            taskInfo = await sqlServerTaskConsumer.CompleteAsync(taskInfo.TaskId, result, taskInfo.RunId, CancellationToken.None);
            Assert.Equal(TaskStatus.Completed, taskInfo.Status);
            Assert.Equal(JsonConvert.SerializeObject(result), taskInfo.Result);
        }

        [Fact]
        public async Task GivenARunningTask_WhenReachMaxRetryCount_ThenResetShouldFail()
        {
            string queueId = Guid.NewGuid().ToString();
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

            string taskId = Guid.NewGuid().ToString();
            short typeId = 1;
            string inputData = "inputData";

            TaskInfo taskInfo = new TaskInfo()
            {
                TaskId = taskId,
                QueueId = queueId,
                TaskTypeId = typeId,
                InputData = inputData,
                MaxRetryCount = 1,
            };

            _ = await sqlServerTaskManager.CreateTaskAsync(taskInfo, CancellationToken.None);
            TaskResultData result = new TaskResultData(TaskResult.Fail, "Result");

            taskInfo = (await sqlServerTaskConsumer.GetNextMessagesAsync(1, 60, CancellationToken.None)).First();
            taskInfo = await sqlServerTaskConsumer.ResetAsync(taskInfo.TaskId, result, taskInfo.RunId, CancellationToken.None);
            taskInfo = (await sqlServerTaskConsumer.GetNextMessagesAsync(1, 60, CancellationToken.None)).First();
            await Assert.ThrowsAsync<TaskAlreadyCompletedException>(async () => await sqlServerTaskConsumer.ResetAsync(taskInfo.TaskId, result, taskInfo.RunId, CancellationToken.None));

            taskInfo = await sqlServerTaskManager.GetTaskAsync(taskInfo.TaskId, CancellationToken.None);
            Assert.Equal(TaskStatus.Completed, taskInfo.Status);
            Assert.Equal(JsonConvert.SerializeObject(result), taskInfo.Result);
        }

        [Fact]
        public async Task GivenCompletedTask_WhenResetTask_ThenResetShouldFail()
        {
            string queueId = Guid.NewGuid().ToString();
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

            string taskId = Guid.NewGuid().ToString();
            short typeId = 1;
            string inputData = "inputData";

            TaskInfo taskInfo = new TaskInfo()
            {
                TaskId = taskId,
                QueueId = queueId,
                TaskTypeId = typeId,
                InputData = inputData,
                MaxRetryCount = 1,
            };

            _ = await sqlServerTaskManager.CreateTaskAsync(taskInfo, CancellationToken.None);
            TaskResultData result = new TaskResultData(TaskResult.Fail, "Result");

            taskInfo = (await sqlServerTaskConsumer.GetNextMessagesAsync(1, 60, CancellationToken.None)).First();
            _ = await sqlServerTaskConsumer.CompleteAsync(taskInfo.TaskId, result, taskInfo.RunId, CancellationToken.None);
            await Assert.ThrowsAsync<TaskAlreadyCompletedException>(async () => await sqlServerTaskConsumer.ResetAsync(taskInfo.TaskId, result, taskInfo.RunId, CancellationToken.None));
        }

        [Fact]
        public async Task GivenARunningTask_WhenUpdateWithWrongRunid_ThenExceptionShouldBeThrew()
        {
            string queueId = Guid.NewGuid().ToString();
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

            string taskId = Guid.NewGuid().ToString();
            short typeId = 1;
            string inputData = "inputData";

            TaskInfo taskInfo = new TaskInfo()
            {
                TaskId = taskId,
                QueueId = queueId,
                TaskTypeId = typeId,
                InputData = inputData,
                MaxRetryCount = 1,
            };

            _ = await sqlServerTaskManager.CreateTaskAsync(taskInfo, CancellationToken.None);
            TaskResultData result = new TaskResultData(TaskResult.Fail, "Result");

            taskInfo = (await sqlServerTaskConsumer.GetNextMessagesAsync(1, 60, CancellationToken.None)).First();
            await Assert.ThrowsAsync<TaskNotExistException>(async () => await sqlServerTaskConsumer.KeepAliveAsync(taskInfo.TaskId, "invalid", CancellationToken.None));
            await Assert.ThrowsAsync<TaskNotExistException>(async () => await sqlServerTaskConsumer.CompleteAsync(taskInfo.TaskId, result, "invalid", CancellationToken.None));
            await Assert.ThrowsAsync<TaskNotExistException>(async () => await sqlServerTaskConsumer.ResetAsync(taskInfo.TaskId, result, "invalid", CancellationToken.None));
        }
    }
}
