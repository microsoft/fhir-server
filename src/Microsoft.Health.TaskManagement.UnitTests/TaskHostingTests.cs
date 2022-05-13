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
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.TaskManagement.UnitTests
{
    public class TaskHostingTests
    {
        private ILogger<TaskHosting> _logger;

        public TaskHostingTests()
        {
            _logger = Substitute.For<ILogger<TaskHosting>>();
        }

        [Fact]
        public async Task GivenValidTasks_WhenTaskHostingStart_ThenTasksShouldBeExecute()
        {
            TestQueueClient queueClient = new TestQueueClient();
            int taskCount = 10;
            List<string> definitions = new List<string>();
            for (int i = 0; i < taskCount; ++i)
            {
                definitions.Add(taskCount.ToString());
            }

            IEnumerable<TaskInfo> tasks = await queueClient.EnqueueAsync(0, definitions.ToArray(), null, false, CancellationToken.None);

            int executedTaskCount = 0;
            TestTaskFactory factory = new TestTaskFactory(t =>
            {
                return new TestTask(
                    async (progress, cancellationToken) =>
                    {
                        Interlocked.Increment(ref executedTaskCount);

                        await Task.Delay(TimeSpan.FromMilliseconds(20));
                        return t.Definition;
                    });
            });

            TaskHosting taskHosting = new TaskHosting(queueClient, factory, _logger);
            taskHosting.PollingFrequencyInSeconds = 0;
            taskHosting.MaxRunningTaskCount = 5;

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            tokenSource.CancelAfter(TimeSpan.FromSeconds(10));
            await taskHosting.StartAsync(0, "test", tokenSource);

            Assert.True(taskCount == executedTaskCount);
            foreach (TaskInfo task in tasks)
            {
                Assert.Equal(TaskStatus.Completed, task.Status);
                Assert.Equal(task.Definition, task.Result);
            }
        }

        [Fact]
        public async Task GivenTaskWithCriticalException_WhenTaskHostingStart_ThenTaskShouldFailWithErrorMessage()
        {
            string errorMessage = "Test error";
            object error = new { error = errorMessage };
            object defaultError = new { message = errorMessage };

            string definition1 = "definition1";
            string definition2 = "definition2";

            TestQueueClient queueClient = new TestQueueClient();
            TaskInfo task1 = (await queueClient.EnqueueAsync(0, new string[] { definition1 }, null, false, CancellationToken.None)).First();
            TaskInfo task2 = (await queueClient.EnqueueAsync(0, new string[] { definition2 }, null, false, CancellationToken.None)).First();

            int executeCount = 0;
            TestTaskFactory factory = new TestTaskFactory(t =>
            {
                if (definition1.Equals(t.Definition))
                {
                    return new TestTask(
                            (progress, token) =>
                            {
                                Interlocked.Increment(ref executeCount);

                                throw new TaskExecutionException(errorMessage, error);
                            });
                }
                else
                {
                    return new TestTask(
                            (progress, token) =>
                            {
                                Interlocked.Increment(ref executeCount);

                                throw new Exception(errorMessage);
                            });
                }
            });

            TaskHosting taskHosting = new TaskHosting(queueClient, factory, _logger);
            taskHosting.PollingFrequencyInSeconds = 0;
            taskHosting.MaxRunningTaskCount = 1;

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            tokenSource.CancelAfter(TimeSpan.FromSeconds(1));
            await taskHosting.StartAsync(0, "test", tokenSource);

            Assert.Equal(2, executeCount);

            Assert.Equal(TaskStatus.Failed, task1.Status);
            Assert.Equal(JsonConvert.SerializeObject(error), task1.Result);
            Assert.Equal(TaskStatus.Failed, task2.Status);
            Assert.Equal(JsonConvert.SerializeObject(defaultError), task2.Result);
        }

        [Fact]
        public async Task GivenAnCrashTask_WhenTaskHostingStart_ThenTaskShouldBeRePickup()
        {
            int executeCount0 = 0;
            TestQueueClient queueClient = new TestQueueClient();
            TestTaskFactory factory = new TestTaskFactory(t =>
            {
                return new TestTask(
                        (progress, token) =>
                        {
                            Interlocked.Increment(ref executeCount0);

                            return Task.FromResult(t.Definition);
                        });
            });

            TaskInfo task1 = (await queueClient.EnqueueAsync(0, new string[] { "task1" }, null, false, CancellationToken.None)).First();
            task1.Status = TaskStatus.Running;
            task1.HeartbeatDateTime = DateTime.Now.AddSeconds(-3);

            TaskHosting taskHosting = new TaskHosting(queueClient, factory, _logger);
            taskHosting.PollingFrequencyInSeconds = 0;
            taskHosting.MaxRunningTaskCount = 1;
            taskHosting.TaskHeartbeatTimeoutThresholdInSeconds = 2;

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            tokenSource.CancelAfter(TimeSpan.FromSeconds(5));
            await taskHosting.StartAsync(0, "test", tokenSource);

            Assert.Equal(TaskStatus.Completed, task1.Status);
            Assert.Equal(1, executeCount0);
        }

        [Fact]
        public async Task GivenAnLongRunningTask_WhenTaskHostingStop_ThenTaskShouldBeCompleted()
        {
            AutoResetEvent autoResetEvent = new AutoResetEvent(false);

            int executeCount0 = 0;
            TestTaskFactory factory = new TestTaskFactory(t =>
            {
                return new TestTask(
                        async (progress, token) =>
                        {
                            autoResetEvent.Set();
                            await Task.Delay(TimeSpan.FromSeconds(10));
                            Interlocked.Increment(ref executeCount0);

                            return t.Definition;
                        });
            });

            TestQueueClient queueClient = new TestQueueClient();
            TaskInfo task1 = (await queueClient.EnqueueAsync(0, new string[] { "task1" }, null, false, CancellationToken.None)).First();
            TaskHosting taskHosting = new TaskHosting(queueClient, factory, _logger);
            taskHosting.PollingFrequencyInSeconds = 0;
            taskHosting.MaxRunningTaskCount = 1;
            taskHosting.TaskHeartbeatTimeoutThresholdInSeconds = 2;

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            Task hostingTask = taskHosting.StartAsync(0, "test", tokenSource);
            autoResetEvent.WaitOne();
            tokenSource.Cancel();

            await hostingTask;

            // If task failcount > MaxRetryCount + 1, the Task should failed with error message.
            Assert.Equal(TaskStatus.Completed, task1.Status);
            Assert.Equal(1, executeCount0);
        }

        [Fact]
        public async Task GivenTaskWithRetriableException_WhenTaskHostingStart_ThenTaskShouldBeRetry()
        {
            int executeCount0 = 0;
            TestTaskFactory factory = new TestTaskFactory(t =>
            {
                return new TestTask(
                    (progress, token) =>
                    {
                        Interlocked.Increment(ref executeCount0);
                        if (executeCount0 <= 1)
                        {
                            throw new RetriableTaskException("test");
                        }

                        return Task.FromResult(t.Result);
                    });
            });

            TestQueueClient queueClient = new TestQueueClient();
            TaskInfo task1 = (await queueClient.EnqueueAsync(0, new string[] { "task1" }, null, false, CancellationToken.None)).First();

            TaskHosting taskHosting = new TaskHosting(queueClient, factory, _logger);
            taskHosting.PollingFrequencyInSeconds = 0;
            taskHosting.MaxRunningTaskCount = 1;
            taskHosting.TaskHeartbeatTimeoutThresholdInSeconds = 2;

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            tokenSource.CancelAfter(TimeSpan.FromSeconds(10));
            await taskHosting.StartAsync(0, "test", tokenSource);

            Assert.Equal(TaskStatus.Completed, task1.Status);
            Assert.Equal(2, executeCount0);
        }

        [Fact]
        public async Task GivenTaskRunning_WhenCancel_ThenTaskShouldBeCancelled()
        {
            AutoResetEvent autoResetEvent = new AutoResetEvent(false);
            TestTaskFactory factory = new TestTaskFactory(t =>
            {
                return new TestTask(
                        async (progress, token) =>
                        {
                            autoResetEvent.Set();

                            while (!token.IsCancellationRequested)
                            {
                                await Task.Delay(TimeSpan.FromMilliseconds(100));
                            }

                            return t.Definition;
                        });
            });

            TestQueueClient queueClient = new TestQueueClient();
            TaskInfo task1 = (await queueClient.EnqueueAsync(0, new string[] { "task1" }, null, false, CancellationToken.None)).First();

            TaskHosting taskHosting = new TaskHosting(queueClient, factory, _logger);
            taskHosting.PollingFrequencyInSeconds = 0;
            taskHosting.MaxRunningTaskCount = 1;

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            tokenSource.CancelAfter(TimeSpan.FromSeconds(5));
            Task hostingTask = taskHosting.StartAsync(0, "test", tokenSource);

            autoResetEvent.WaitOne();
            await queueClient.CancelTaskAsync(0, task1.GroupId, CancellationToken.None);

            await hostingTask;

            Assert.Equal(TaskStatus.Completed, task1.Status);
        }

        [Fact]
        public async Task GivenTaskRunning_WhenUpdateCurrentResult_ThenCurrentResultShouldBePersisted()
        {
            AutoResetEvent autoResetEvent1 = new AutoResetEvent(false);
            AutoResetEvent autoResetEvent2 = new AutoResetEvent(false);
            TestTaskFactory factory = new TestTaskFactory(t =>
            {
                return new TestTask(
                        async (progress, token) =>
                        {
                            progress.Report("Progress");
                            await Task.Delay(TimeSpan.FromSeconds(2));
                            autoResetEvent1.Set();
                            await Task.Delay(TimeSpan.FromSeconds(1));

                            return t.Definition;
                        });
            });

            TestQueueClient queueClient = new TestQueueClient();
            TaskInfo task1 = (await queueClient.EnqueueAsync(0, new string[] { "task1" }, null, false, CancellationToken.None)).First();

            TaskHosting taskHosting = new TaskHosting(queueClient, factory, _logger);
            taskHosting.PollingFrequencyInSeconds = 0;
            taskHosting.MaxRunningTaskCount = 1;
            taskHosting.TaskHeartbeatIntervalInSeconds = 1;

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            tokenSource.CancelAfter(TimeSpan.FromSeconds(5));
            Task hostingTask = taskHosting.StartAsync(0, "test", tokenSource);

            autoResetEvent1.WaitOne();
            Assert.Equal("Progress", task1.Result);

            await hostingTask;

            Assert.Equal(TaskStatus.Completed, task1.Status);
        }

        [Fact]
        public async Task GivenRandomFailuresInQueueClient_WhenStartHosting_ThenAllTasksShouldBeCompleted()
        {
            TestTaskFactory factory = new TestTaskFactory(t =>
            {
                return new TestTask(
                        async (progress, token) =>
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(10));
                            return t.Definition;
                        });
            });

            TestQueueClient queueClient = new TestQueueClient();
            int randomNumber = 0;
            int errorNumber = 0;
            Action faultAction = () =>
            {
                Interlocked.Increment(ref randomNumber);

                if (randomNumber % 2 == 0)
                {
                    Interlocked.Increment(ref errorNumber);
                    throw new InvalidOperationException();
                }
            };

            queueClient.CompleteFaultAction = faultAction;
            queueClient.DequeueFaultAction = faultAction;
            queueClient.HeartbeatFaultAction = faultAction;

            List<string> definitions = new List<string>();
            for (int i = 0; i < 100; ++i)
            {
                definitions.Add(i.ToString());
            }

            var tasks = await queueClient.EnqueueAsync(0, definitions.ToArray(), null, false, CancellationToken.None);

            TaskHosting taskHosting = new TaskHosting(queueClient, factory, _logger);
            taskHosting.PollingFrequencyInSeconds = 0;
            taskHosting.MaxRunningTaskCount = 50;
            taskHosting.TaskHeartbeatIntervalInSeconds = 1;
            taskHosting.TaskHeartbeatTimeoutThresholdInSeconds = 2;

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            tokenSource.CancelAfter(TimeSpan.FromSeconds(10));
            await taskHosting.StartAsync(0, "test", tokenSource);

            Assert.True(tasks.All(t => t.Status == TaskStatus.Completed));
            Assert.True(errorNumber > 0);
        }
    }
}
