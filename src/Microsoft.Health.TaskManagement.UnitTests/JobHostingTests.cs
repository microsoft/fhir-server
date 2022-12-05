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
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.JobManagement.UnitTests
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.AnonymizedExport)]
    public class JobHostingTests
    {
        private ILogger<JobHosting> _logger;

        public JobHostingTests()
        {
            _logger = Substitute.For<ILogger<JobHosting>>();
        }

        [Fact]
        public async Task GivenValidJobs_WhenJobHostingStart_ThenJobsShouldBeExecute()
        {
            TestQueueClient queueClient = new TestQueueClient();
            int jobCount = 10;
            List<string> definitions = new List<string>();
            for (int i = 0; i < jobCount; ++i)
            {
                definitions.Add(jobCount.ToString());
            }

            IEnumerable<JobInfo> jobs = await queueClient.EnqueueAsync(0, definitions.ToArray(), null, false, false, CancellationToken.None);

            int executedJobCount = 0;
            TestJobFactory factory = new TestJobFactory(t =>
            {
                return new TestJob(
                    async (progress, cancellationToken) =>
                    {
                        Interlocked.Increment(ref executedJobCount);

                        await Task.Delay(TimeSpan.FromMilliseconds(20));
                        return t.Definition;
                    });
            });

            JobHosting jobHosting = new JobHosting(queueClient, factory, _logger);
            jobHosting.PollingFrequencyInSeconds = 0;
            jobHosting.MaxRunningJobCount = 5;

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            tokenSource.CancelAfter(TimeSpan.FromSeconds(10));
            await jobHosting.StartAsync(0, "test", tokenSource);

            Assert.True(jobCount == executedJobCount);
            foreach (JobInfo job in jobs)
            {
                Assert.Equal(JobStatus.Completed, job.Status);
                Assert.Equal(job.Definition, job.Result);
            }
        }

        [Fact]
        public async Task GivenJobWithCriticalException_WhenJobHostingStart_ThenJobShouldFailWithErrorMessage()
        {
            string errorMessage = "Test error";
            object error = new { error = errorMessage };

            string definition1 = "definition1";
            string definition2 = "definition2";

            TestQueueClient queueClient = new TestQueueClient();
            JobInfo job1 = (await queueClient.EnqueueAsync(0, new string[] { definition1 }, null, false, false, CancellationToken.None)).First();
            JobInfo job2 = (await queueClient.EnqueueAsync(0, new string[] { definition2 }, null, false, false, CancellationToken.None)).First();

            int executeCount = 0;
            TestJobFactory factory = new TestJobFactory(t =>
            {
                if (definition1.Equals(t.Definition))
                {
                    return new TestJob(
                            (progress, token) =>
                            {
                                Interlocked.Increment(ref executeCount);

                                throw new JobExecutionException(errorMessage, error);
                            });
                }
                else
                {
                    return new TestJob(
                            (progress, token) =>
                            {
                                Interlocked.Increment(ref executeCount);
                                throw new Exception(errorMessage);
                            });
                }
            });

            JobHosting jobHosting = new JobHosting(queueClient, factory, _logger);
            jobHosting.PollingFrequencyInSeconds = 0;
            jobHosting.MaxRunningJobCount = 1;

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            tokenSource.CancelAfter(TimeSpan.FromSeconds(1));
            await jobHosting.StartAsync(0, "test", tokenSource);

            Assert.Equal(2, executeCount);

            Assert.Equal(JobStatus.Failed, job1.Status);
            Assert.Equal(JsonConvert.SerializeObject(error), job1.Result);
            Assert.Equal(JobStatus.Failed, job2.Status);

            // Job2's error includes the stack trace with can't be easily added to the expected value, so we just look for the message.
            Assert.Contains(errorMessage, job2.Result);
        }

        [Fact]
        public async Task GivenAnCrashJob_WhenJobHostingStart_ThenJobShouldBeRePickup()
        {
            int executeCount0 = 0;
            TestQueueClient queueClient = new TestQueueClient();
            TestJobFactory factory = new TestJobFactory(t =>
            {
                return new TestJob(
                        (progress, token) =>
                        {
                            Interlocked.Increment(ref executeCount0);

                            return Task.FromResult(t.Definition);
                        });
            });

            JobInfo job1 = (await queueClient.EnqueueAsync(0, new string[] { "job1" }, null, false, false, CancellationToken.None)).First();
            job1.Status = JobStatus.Running;
            job1.HeartbeatDateTime = DateTime.Now.AddSeconds(-3);

            JobHosting jobHosting = new JobHosting(queueClient, factory, _logger);
            jobHosting.PollingFrequencyInSeconds = 0;
            jobHosting.MaxRunningJobCount = 1;
            jobHosting.JobHeartbeatTimeoutThresholdInSeconds = 2;

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            tokenSource.CancelAfter(TimeSpan.FromSeconds(5));
            await jobHosting.StartAsync(0, "test", tokenSource);

            Assert.Equal(JobStatus.Completed, job1.Status);
            Assert.Equal(1, executeCount0);
        }

        [Fact]
        public async Task GivenAnLongRunningJob_WhenJobHostingStop_ThenJobShouldBeCompleted()
        {
            AutoResetEvent autoResetEvent = new AutoResetEvent(false);

            int executeCount0 = 0;
            TestJobFactory factory = new TestJobFactory(t =>
            {
                return new TestJob(
                        async (progress, token) =>
                        {
                            autoResetEvent.Set();
                            await Task.Delay(TimeSpan.FromSeconds(10), token);
                            Interlocked.Increment(ref executeCount0);

                            return t.Definition;
                        });
            });

            TestQueueClient queueClient = new TestQueueClient();
            JobInfo job1 = (await queueClient.EnqueueAsync(0, new string[] { "job1" }, null, false, false, CancellationToken.None)).First();
            JobHosting jobHosting = new JobHosting(queueClient, factory, _logger);
            jobHosting.PollingFrequencyInSeconds = 0;
            jobHosting.MaxRunningJobCount = 1;
            jobHosting.JobHeartbeatTimeoutThresholdInSeconds = 2;

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            Task hostingTask = jobHosting.StartAsync(0, "test", tokenSource);
            autoResetEvent.WaitOne();
            tokenSource.Cancel();

            await hostingTask;

            // If job failcount > MaxRetryCount + 1, the Job should failed with error message.
            Assert.Equal(JobStatus.Completed, job1.Status);
            Assert.Equal(1, executeCount0);
        }

        [Fact]
        public async Task GivenJobWithRetriableException_WhenJobHostingStart_ThenJobShouldBeRetry()
        {
            int executeCount0 = 0;
            TestJobFactory factory = new TestJobFactory(t =>
            {
                return new TestJob(
                    (progress, token) =>
                    {
                        Interlocked.Increment(ref executeCount0);
                        if (executeCount0 <= 1)
                        {
                            throw new RetriableJobException("test");
                        }

                        return Task.FromResult(t.Result);
                    });
            });

            TestQueueClient queueClient = new TestQueueClient();
            JobInfo job1 = (await queueClient.EnqueueAsync(0, new string[] { "task1" }, null, false, false, CancellationToken.None)).First();

            JobHosting jobHosting = new JobHosting(queueClient, factory, _logger);
            jobHosting.PollingFrequencyInSeconds = 0;
            jobHosting.MaxRunningJobCount = 1;
            jobHosting.JobHeartbeatTimeoutThresholdInSeconds = 2;

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            tokenSource.CancelAfter(TimeSpan.FromSeconds(10));
            await jobHosting.StartAsync(0, "test", tokenSource);

            Assert.Equal(JobStatus.Completed, job1.Status);
            Assert.Equal(2, executeCount0);
        }

        [Fact]
        public async Task GivenJobRunning_WhenCancel_ThenJobShouldBeCancelled()
        {
            AutoResetEvent autoResetEvent = new AutoResetEvent(false);
            TestJobFactory factory = new TestJobFactory(t =>
            {
                return new TestJob(
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
            JobInfo job1 = (await queueClient.EnqueueAsync(0, new string[] { "task1" }, null, false, false, CancellationToken.None)).First();

            JobHosting jobHosting = new JobHosting(queueClient, factory, _logger);
            jobHosting.PollingFrequencyInSeconds = 0;
            jobHosting.MaxRunningJobCount = 1;

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            tokenSource.CancelAfter(TimeSpan.FromSeconds(5));
            Task hostingTask = jobHosting.StartAsync(0, "test", tokenSource);

            autoResetEvent.WaitOne();
            await queueClient.CancelJobByGroupIdAsync(0, job1.GroupId, CancellationToken.None);

            await hostingTask;

            Assert.Equal(JobStatus.Completed, job1.Status);
        }

        [Fact]
        public async Task GivenJobRunning_WhenUpdateCurrentResult_ThenCurrentResultShouldBePersisted()
        {
            AutoResetEvent autoResetEvent1 = new AutoResetEvent(false);
            AutoResetEvent autoResetEvent2 = new AutoResetEvent(false);
            TestJobFactory factory = new TestJobFactory(t =>
            {
                return new TestJob(
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
            JobInfo job1 = (await queueClient.EnqueueAsync(0, new string[] { "task1" }, null, false, false, CancellationToken.None)).First();

            JobHosting jobHosting = new JobHosting(queueClient, factory, _logger);
            jobHosting.PollingFrequencyInSeconds = 0;
            jobHosting.MaxRunningJobCount = 1;
            jobHosting.JobHeartbeatIntervalInSeconds = 1;

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            tokenSource.CancelAfter(TimeSpan.FromSeconds(5));
            Task hostingTask = jobHosting.StartAsync(0, "test", tokenSource);

            autoResetEvent1.WaitOne();
            Assert.Equal("Progress", job1.Result);

            await hostingTask;

            Assert.Equal(JobStatus.Completed, job1.Status);
        }

        [Fact]
        public async Task GivenRandomFailuresInQueueClient_WhenStartHosting_ThenAllTasksShouldBeCompleted()
        {
            TestJobFactory factory = new TestJobFactory(t =>
            {
                return new TestJob(
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

            var jobs = await queueClient.EnqueueAsync(0, definitions.ToArray(), null, false, false, CancellationToken.None);

            JobHosting jobHosting = new JobHosting(queueClient, factory, _logger);
            jobHosting.PollingFrequencyInSeconds = 0;
            jobHosting.MaxRunningJobCount = 50;
            jobHosting.JobHeartbeatIntervalInSeconds = 1;
            jobHosting.JobHeartbeatTimeoutThresholdInSeconds = 2;

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            tokenSource.CancelAfter(TimeSpan.FromSeconds(10));
            await jobHosting.StartAsync(0, "test", tokenSource);

            Assert.True(jobs.All(t => t.Status == JobStatus.Completed));
            Assert.True(errorNumber > 0);
        }
    }
}
