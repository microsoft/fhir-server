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
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.TaskManagement;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;
using TaskStatus = Microsoft.Health.Fhir.Core.Features.TaskManagement.TaskStatus;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.TaskManagement
{
    public class TaskHostingTests
    {
        private ILogger<TaskHosting> _logger;

        public TaskHostingTests()
        {
            _logger = Substitute.For<ILogger<TaskHosting>>();
        }

        [Fact]
        public async Task GivenValidTasks_WhenTaskHostingStart_TasksShouldBeExecute()
        {
            int taskCount = 100;
            string resultMessage = "success";
            List<TaskInfo> taskInfos = new List<TaskInfo>();
            for (int i = 0; i < taskCount; ++i)
            {
                TaskInfo taskInfo = new TaskInfo();
                taskInfo.TaskId = Guid.NewGuid().ToString();
                taskInfo.TaskTypeId = 0;

                taskInfos.Add(taskInfo);
            }

            int executedTaskCount = 0;
            TestTaskConsumer consumer = new TestTaskConsumer(taskInfos.ToArray());
            TestTaskFactory factory = new TestTaskFactory(t =>
            {
                return new TestTask(
                    async () =>
                    {
                        Interlocked.Increment(ref executedTaskCount);

                        await Task.Delay(TimeSpan.FromMilliseconds(20));
                        return new TaskResultData(TaskResult.Success, resultMessage);
                    },
                    null,
                    t.RunId);
            });

            TaskHosting taskHosting = new TaskHosting(consumer, factory, _logger);
            taskHosting.PollingFrequencyInSeconds = 0;
            taskHosting.MaxRunningTaskCount = 5;

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            tokenSource.CancelAfter(TimeSpan.FromSeconds(5));
            await taskHosting.StartAsync(tokenSource);

            Assert.Equal(taskCount, executedTaskCount);
            foreach (string resultString in taskInfos.Select(t => t.Result))
            {
                TaskResultData result = JsonConvert.DeserializeObject<TaskResultData>(resultString);
                Assert.Equal(TaskResult.Success, result.Result);
                Assert.Equal(resultMessage, result.ResultData);
            }
        }

        [Fact]
        public async Task GivenTaskWithCriticalException_WhenTaskHostingStart_TaskShouldFailWithErrorMessage()
        {
            string errorMessage = "Test error";

            TaskInfo taskInfo0 = new TaskInfo();
            taskInfo0.TaskId = Guid.NewGuid().ToString();
            taskInfo0.TaskTypeId = 0;

            int executeCount0 = 0;
            TestTaskConsumer consumer = new TestTaskConsumer(new TaskInfo[] { taskInfo0 });
            TestTaskFactory factory = new TestTaskFactory(t =>
            {
                return new TestTask(
                        () =>
                        {
                            Interlocked.Increment(ref executeCount0);

                            throw new Exception(errorMessage);
                        },
                        null,
                        t.RunId);
            });

            TaskHosting taskHosting = new TaskHosting(consumer, factory, _logger);
            taskHosting.PollingFrequencyInSeconds = 0;
            taskHosting.MaxRunningTaskCount = 1;

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            tokenSource.CancelAfter(TimeSpan.FromSeconds(1));
            await taskHosting.StartAsync(tokenSource);

            Assert.Equal(1, executeCount0);

            // If task failcount > MaxRetryCount + 1, the Task should failed with error message.
            TaskResultData taskResult0 = JsonConvert.DeserializeObject<TaskResultData>(taskInfo0.Result);
            Assert.Equal(TaskResult.Fail, taskResult0.Result);
            Assert.Equal(errorMessage, taskResult0.ResultData);
        }

        [Fact]
        public async Task GivenAnCrashTask_WhenTaskHostingStart_TaskShouldBeRePickup()
        {
            TaskInfo taskInfo0 = new TaskInfo();
            taskInfo0.TaskId = Guid.NewGuid().ToString();
            taskInfo0.TaskTypeId = 0;
            taskInfo0.Status = TaskStatus.Running;

            int executeCount0 = 0;
            TestTaskConsumer consumer = new TestTaskConsumer(new TaskInfo[] { taskInfo0 });
            TestTaskFactory factory = new TestTaskFactory(t =>
            {
                return new TestTask(
                        () =>
                        {
                            Interlocked.Increment(ref executeCount0);

                            return Task.FromResult(new TaskResultData(TaskResult.Success, string.Empty));
                        },
                        null,
                        t.RunId);
            });

            TaskHosting taskHosting = new TaskHosting(consumer, factory, _logger);
            taskHosting.PollingFrequencyInSeconds = 0;
            taskHosting.MaxRunningTaskCount = 1;
            taskHosting.TaskHeartbeatTimeoutThresholdInSeconds = 2;

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            tokenSource.CancelAfter(TimeSpan.FromSeconds(5));
            await taskHosting.StartAsync(tokenSource);

            Assert.Equal(1, executeCount0);

            // If task failcount > MaxRetryCount + 1, the Task should failed with error message.
            TaskResultData taskResult0 = JsonConvert.DeserializeObject<TaskResultData>(taskInfo0.Result);
            Assert.Equal(TaskResult.Success, taskResult0.Result);
            Assert.Equal(1, executeCount0);
        }

        [Fact]
        public async Task GivenAnLongRunningTask_WhenTaskHostingStop_TaskShouldBeCompleted()
        {
            TaskInfo taskInfo0 = new TaskInfo();
            taskInfo0.TaskId = Guid.NewGuid().ToString();
            taskInfo0.TaskTypeId = 0;
            taskInfo0.Status = TaskStatus.Running;

            int executeCount0 = 0;
            TestTaskConsumer consumer = new TestTaskConsumer(new TaskInfo[] { taskInfo0 });
            TestTaskFactory factory = new TestTaskFactory(t =>
            {
                return new TestTask(
                        async () =>
                        {
                            await Task.Delay(TimeSpan.FromSeconds(10));
                            Interlocked.Increment(ref executeCount0);

                            return new TaskResultData(TaskResult.Success, string.Empty);
                        },
                        null,
                        t.RunId);
            });

            TaskHosting taskHosting = new TaskHosting(consumer, factory, _logger);
            taskHosting.PollingFrequencyInSeconds = 0;
            taskHosting.MaxRunningTaskCount = 1;
            taskHosting.TaskHeartbeatTimeoutThresholdInSeconds = 2;

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            tokenSource.CancelAfter(TimeSpan.FromSeconds(2));
            await taskHosting.StartAsync(tokenSource);

            Assert.Equal(1, executeCount0);

            // If task failcount > MaxRetryCount + 1, the Task should failed with error message.
            TaskResultData taskResult0 = JsonConvert.DeserializeObject<TaskResultData>(taskInfo0.Result);
            Assert.Equal(TaskResult.Success, taskResult0.Result);
            Assert.Equal(1, executeCount0);
        }

        [Fact]
        public async Task GivenTaskCrash_WhenTaskHostingRepickupTask_FirstTasksShouldBeCancelled()
        {
            string resultMessage = "success";
            List<TaskInfo> taskInfos = new List<TaskInfo>();
            TaskInfo taskInfo = new TaskInfo();
            taskInfo.TaskId = Guid.NewGuid().ToString();
            taskInfo.TaskTypeId = 0;

            taskInfos.Add(taskInfo);

            TestTaskConsumer consumer = new TestTaskConsumer(taskInfos.ToArray());
            bool isCancelled = false;
            TestTaskFactory factory = new TestTaskFactory(t =>
            {
                return new TestTask(
                    async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10));
                        return new TaskResultData(TaskResult.Success, resultMessage);
                    },
                    async () =>
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(20));
                        isCancelled = true;
                    },
                    Guid.NewGuid().ToString());
            });

            TaskHosting taskHosting = new TaskHosting(consumer, factory, _logger);
            taskHosting.PollingFrequencyInSeconds = 0;

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            tokenSource.CancelAfter(TimeSpan.FromSeconds(5));
            await taskHosting.StartAsync(tokenSource);

            Assert.True(isCancelled);
        }

        [Fact]
        public async Task GivenTaskWithRetriableException_WhenTaskHostingStart_TaskShouldBeRetry()
        {
            string errorMessage = "Test error";
            int maxRetryCount = 2;

            TaskInfo taskInfo0 = new TaskInfo();
            taskInfo0.TaskId = Guid.NewGuid().ToString();
            taskInfo0.TaskTypeId = 0;

            TaskInfo taskInfo1 = new TaskInfo();
            taskInfo1.TaskId = Guid.NewGuid().ToString();
            taskInfo1.TaskTypeId = 1;

            int executeCount0 = 0;
            int executeCount1 = 0;
            TestTaskConsumer consumer = new TestTaskConsumer(new TaskInfo[] { taskInfo0, taskInfo1 }, maxRetryCount);
            TestTaskFactory factory = new TestTaskFactory(t =>
            {
                if (t.TaskTypeId == 0)
                {
                    return new TestTask(
                        () =>
                        {
                            Interlocked.Increment(ref executeCount0);

                            throw new RetriableTaskException(errorMessage);
                        },
                        null,
                        t.RunId);
                }
                else
                {
                    return new TestTask(
                        () =>
                        {
                            Interlocked.Increment(ref executeCount1);

                            if (executeCount1 < maxRetryCount + 1)
                            {
                                throw new RetriableTaskException(errorMessage);
                            }

                            return Task.FromResult<TaskResultData>(new TaskResultData(TaskResult.Success, string.Empty));
                        },
                        null,
                        t.RunId);
                }
            });

            TaskHosting taskHosting = new TaskHosting(consumer, factory, _logger);
            taskHosting.PollingFrequencyInSeconds = 0;
            taskHosting.MaxRunningTaskCount = 1;

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            tokenSource.CancelAfter(TimeSpan.FromSeconds(5));
            await taskHosting.StartAsync(tokenSource);

            Assert.Equal(maxRetryCount + 1, executeCount0);
            Assert.Equal(maxRetryCount + 1, executeCount1);

            // If task failcount > MaxRetryCount + 1, the Task should failed with error message.
            TaskResultData taskResult0 = JsonConvert.DeserializeObject<TaskResultData>(taskInfo0.Result);
            Assert.Equal(TaskResult.Fail, taskResult0.Result);
            Assert.Equal(errorMessage, taskResult0.ResultData);

            // If task failcount < MaxRetryCount + 1, the Task should success.
            TaskResultData taskResult1 = JsonConvert.DeserializeObject<TaskResultData>(taskInfo1.Result);
            Assert.Equal(TaskResult.Success, taskResult1.Result);
        }

        [Fact]
        public async Task GivenTaskThrowException_WhenTaskHostingStart_TaskHostingShouldKeepRunning()
        {
            int taskCount = 1000;
            List<TaskInfo> taskInfos = new List<TaskInfo>();
            for (int i = 0; i < taskCount; ++i)
            {
                TaskInfo taskInfo = new TaskInfo();
                taskInfo.TaskId = Guid.NewGuid().ToString();
                taskInfo.TaskTypeId = i % 10 == 0 ? 1 : 0;

                taskInfos.Add(taskInfo);
            }

            int executedTaskCount = 0;
            int failedTaskCount = 0;
            TestTaskConsumer consumer = new TestTaskConsumer(taskInfos.ToArray());
            TestTaskFactory factory = new TestTaskFactory(t =>
            {
                if (t.TaskTypeId == 0)
                {
                    return new TestTask(
                        async () =>
                        {
                            Interlocked.Increment(ref executedTaskCount);

                            await Task.Delay(TimeSpan.FromMilliseconds(10));
                            return new TaskResultData(TaskResult.Success, string.Empty);
                        },
                        null,
                        t.RunId);
                }
                else
                {
                    return new TestTask(
                        () =>
                        {
                            Interlocked.Increment(ref failedTaskCount);

                            throw new Exception();
                        },
                        null,
                        t.RunId);
                }
            });

            TaskHosting taskHosting = new TaskHosting(consumer, factory, _logger);
            taskHosting.PollingFrequencyInSeconds = 0;
            taskHosting.MaxRunningTaskCount = 5;

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            tokenSource.CancelAfter(TimeSpan.FromSeconds(20));
            await taskHosting.StartAsync(tokenSource);

            Assert.Equal(900, executedTaskCount);
            Assert.Equal(100, failedTaskCount);

            foreach (TaskInfo taskInfo in taskInfos)
            {
                TaskResultData taskResult = JsonConvert.DeserializeObject<TaskResultData>(taskInfo.Result);

                if (taskInfo.TaskTypeId == 0)
                {
                    Assert.Equal(TaskResult.Success, taskResult.Result);
                }
                else
                {
                    Assert.Equal(TaskResult.Fail, taskResult.Result);
                }
            }
        }

        [Fact]
        public async Task GivenTempUnavailableConsumer_WhenTaskHostingStart_TaskHostingShouldKeepRunning()
        {
            Random random = new Random();
            int taskCount = 1000;
            List<TaskInfo> taskInfos = new List<TaskInfo>();

            for (int i = 0; i < taskCount; ++i)
            {
                TaskInfo taskInfo = new TaskInfo();
                taskInfo.TaskId = Guid.NewGuid().ToString();
                taskInfo.TaskTypeId = i % 10 == 0 ? 1 : 0;

                taskInfos.Add(taskInfo);
            }

            int faultInjected = 0;
            TestTaskConsumer consumer = new TestTaskConsumer(
                taskInfos.ToArray(),
                faultInjectionAction: (method) =>
                {
                    if (random.NextDouble() < 0.1)
                    {
                        Interlocked.Increment(ref faultInjected);
                        throw new Exception($"Injected Exception in {method}");
                    }
                });

            TestTaskFactory factory = new TestTaskFactory(t =>
            {
                if (t.TaskTypeId == 0)
                {
                    return new TestTask(
                        async () =>
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(1));
                            return new TaskResultData(TaskResult.Success, string.Empty);
                        },
                        null,
                        t.RunId);
                }
                else
                {
                    return new TestTask(
                        () =>
                        {
                            throw new Exception();
                        },
                        null,
                        t.RunId);
                }
            });

            TaskHosting taskHosting = new TaskHosting(consumer, factory, _logger);
            taskHosting.PollingFrequencyInSeconds = 0;
            taskHosting.MaxRunningTaskCount = 20;
            taskHosting.TaskHeartbeatTimeoutThresholdInSeconds = 1;

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            tokenSource.CancelAfter(TimeSpan.FromSeconds(20));
            await taskHosting.StartAsync(tokenSource);

            foreach (TaskInfo taskInfo in taskInfos)
            {
                TaskResultData taskResult = JsonConvert.DeserializeObject<TaskResultData>(taskInfo.Result);

                if (taskInfo.TaskTypeId == 0)
                {
                    Assert.Equal(TaskResult.Success, taskResult.Result);
                }
                else
                {
                    Assert.Equal(TaskResult.Fail, taskResult.Result);
                }
            }
        }
    }
}
