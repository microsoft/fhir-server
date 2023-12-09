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
                definitions.Add(i.ToString());
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

            tokenSource.CancelAfter(TimeSpan.FromSeconds(2));
            await jobHosting.ExecuteAsync(0, "test", tokenSource);

            Assert.Equal(jobCount, executedJobCount);
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
            string groupDefinition1 = "groupDefinition1";
            string groupDefinition2 = "groupDefinition2";

            TestQueueClient queueClient = new TestQueueClient();
            JobInfo job1 = (await queueClient.EnqueueAsync(0, new string[] { definition1 }, 1, false, false, CancellationToken.None)).First();
            JobInfo job2 = (await queueClient.EnqueueAsync(0, new string[] { definition2 }, 2, false, false, CancellationToken.None)).First();

            JobInfo jobGroup1 = (await queueClient.EnqueueAsync(0, new string[] { groupDefinition1 }, 1, false, false, CancellationToken.None)).First();
            JobInfo jobGroup2 = (await queueClient.EnqueueAsync(0, new string[] { groupDefinition2 }, 2, false, false, CancellationToken.None)).First();

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
                else if (definition2.Equals(t.Definition))
                {
                    return new TestJob(
                        (progress, token) =>
                        {
                            Interlocked.Increment(ref executeCount);
                            throw new Exception(errorMessage);
                        });
                }
                else
                {
                    return new TestJob(
                        (progress, token) =>
                        {
                            return Task.FromResult("end");
                        });
                }
            });

            JobHosting jobHosting = new JobHosting(queueClient, factory, _logger);
            jobHosting.PollingFrequencyInSeconds = 0;
            jobHosting.MaxRunningJobCount = 1;

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            tokenSource.CancelAfter(TimeSpan.FromSeconds(1));
            await jobHosting.ExecuteAsync(0, "test", tokenSource);

            Assert.Equal(2, executeCount);

            Assert.Equal(JobStatus.Failed, job1.Status);
            Assert.Equal(JsonConvert.SerializeObject(error), job1.Result);
            Assert.Equal(JobStatus.Completed, jobGroup1.Status);

            Assert.Equal(JobStatus.Failed, job2.Status);

            // Job2's error includes the stack trace with can't be easily added to the expected value, so we just look for the message.
            Assert.Contains(errorMessage, job2.Result);
            Assert.Equal(JobStatus.Cancelled, jobGroup2.Status);
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
            jobHosting.JobHeartbeatTimeoutThresholdInSeconds = 1;

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            tokenSource.CancelAfter(TimeSpan.FromSeconds(2));
            await jobHosting.ExecuteAsync(0, "test", tokenSource);

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
                            await Task.Delay(TimeSpan.FromSeconds(1), token);
                            Interlocked.Increment(ref executeCount0);

                            return t.Definition;
                        });
            });

            TestQueueClient queueClient = new TestQueueClient();
            JobInfo job1 = (await queueClient.EnqueueAsync(0, new string[] { "job1" }, null, false, false, CancellationToken.None)).First();
            JobHosting jobHosting = new JobHosting(queueClient, factory, _logger);
            jobHosting.PollingFrequencyInSeconds = 0;
            jobHosting.MaxRunningJobCount = 1;
            jobHosting.JobHeartbeatTimeoutThresholdInSeconds = 1;

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            Task hostingTask = jobHosting.ExecuteAsync(0, "test", tokenSource);
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
            jobHosting.JobHeartbeatTimeoutThresholdInSeconds = 1;

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            tokenSource.CancelAfter(TimeSpan.FromSeconds(2));
            await jobHosting.ExecuteAsync(0, "test", tokenSource);

            Assert.Equal(JobStatus.Completed, job1.Status);
            Assert.Equal(2, executeCount0);
        }

        [Fact]
        public async Task GivenJobWithInvalidOperationException_WhenJobHostingStart_ThenJobFail()
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
                            throw new InvalidOperationException("test");
                        }

                        return Task.FromResult(t.Result);
                    });
            });

            TestQueueClient queueClient = new TestQueueClient();
            JobInfo job1 = (await queueClient.EnqueueAsync(0, new string[] { "task1" }, null, false, false, CancellationToken.None)).First();

            JobHosting jobHosting = new JobHosting(queueClient, factory, _logger);
            jobHosting.PollingFrequencyInSeconds = 0;
            jobHosting.MaxRunningJobCount = 1;
            jobHosting.JobHeartbeatTimeoutThresholdInSeconds = 1;

            CancellationTokenSource tokenSource = new CancellationTokenSource();

            tokenSource.CancelAfter(TimeSpan.FromSeconds(2));
            await jobHosting.ExecuteAsync(0, "test", tokenSource);

            Assert.Equal(JobStatus.Failed, job1.Status);
            Assert.Equal(1, executeCount0);
        }

        [Theory]
        [InlineData(typeof(OperationCanceledException))]
        [InlineData(typeof(TaskCanceledException))]
        public async Task GivenJobWithCanceledException_WhenJobHostingStart_ThenJobShouldBeCanceled(Type exceptionType)
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
                            throw (Exception)Activator.CreateInstance(exceptionType, "test");
                        }

                        return Task.FromResult(t.Result);
                    });
            });

            TestQueueClient queueClient = new TestQueueClient();
            JobInfo job1 = (await queueClient.EnqueueAsync(0, new string[] { "task1" }, null, false, false, CancellationToken.None)).First();

            JobHosting jobHosting = new JobHosting(queueClient, factory, _logger);
            jobHosting.PollingFrequencyInSeconds = 0;
            jobHosting.MaxRunningJobCount = 1;
            jobHosting.JobHeartbeatTimeoutThresholdInSeconds = 15;

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            tokenSource.CancelAfter(TimeSpan.FromSeconds(2));
            await jobHosting.ExecuteAsync(0, "test", tokenSource);

            Assert.Equal(JobStatus.Cancelled, job1.Status);
            Assert.Equal(1, executeCount0);
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
            jobHosting.JobHeartbeatIntervalInSeconds = 1;
            jobHosting.MaxRunningJobCount = 1;

            CancellationTokenSource tokenSource = new CancellationTokenSource();
            tokenSource.CancelAfter(TimeSpan.FromSeconds(2));
            Task hostingTask = jobHosting.ExecuteAsync(0, "test", tokenSource);

            autoResetEvent.WaitOne();
            await queueClient.CancelJobByGroupIdAsync(0, job1.GroupId, CancellationToken.None);

            await hostingTask;

            Assert.Equal(JobStatus.Completed, job1.Status);
        }

        [Fact]
        public async Task GivenJobRunning_WhenUpdateCurrentResult_ThenCurrentResultShouldBePersisted()
        {
            AutoResetEvent autoResetEvent = new AutoResetEvent(false);
            TestJobFactory factory = new TestJobFactory(t =>
            {
                return new TestJob(
                        async (progress, token) =>
                        {
                            progress.Report("Progress");
                            await Task.Delay(TimeSpan.FromSeconds(1));
                            autoResetEvent.Set();
                            await Task.Delay(TimeSpan.FromSeconds(0.5));

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
            tokenSource.CancelAfter(TimeSpan.FromSeconds(2));
            Task hostingTask = jobHosting.ExecuteAsync(0, "test", tokenSource, true);

            autoResetEvent.WaitOne();
            Assert.Equal("Progress", job1.Result);

            await hostingTask;

            Assert.Equal(JobStatus.Completed, job1.Status);
        }

        [Fact]
        public async Task GivenRandomFailuresInQueueClient_WhenStartHosting_ThenAllTasksShouldBeCompleted()
        {
            var factory = new TestJobFactory(t =>
            {
                return new TestJob(
                        async (progress, token) =>
                        {
                            await Task.Delay(TimeSpan.FromSeconds(0.01));
                            return t.Definition;
                        });
            });

            var queueClient = new TestQueueClient();
            var randomNumber = 0;
            var completeErrorNumber = 0;
            Action completeFaultAction = () =>
            {
                if (Interlocked.Increment(ref randomNumber) % 3 == 0)
                {
                    Interlocked.Increment(ref completeErrorNumber);
                    Task.Delay(TimeSpan.FromSeconds(0.001)).Wait();
                    throw new InvalidOperationException();
                }
            };

            var dequeueErrorNumber = 0;
            Action dequeueFaultAction = () =>
            {
                if (Interlocked.Increment(ref randomNumber) % 3 == 0)
                {
                    Interlocked.Increment(ref dequeueErrorNumber);
                    Task.Delay(TimeSpan.FromSeconds(0.001)).Wait();
                    throw new InvalidOperationException();
                }
            };

            var heartbeatErrorNumber = 0;
            Action heartbeatFaultAction = () =>
            {
                if (Interlocked.Increment(ref randomNumber) % 3 == 0)
                {
                    Interlocked.Increment(ref heartbeatErrorNumber);
                    Task.Delay(TimeSpan.FromSeconds(0.001)).Wait();
                    throw new InvalidOperationException();
                }
            };

            queueClient.CompleteFaultAction = completeFaultAction;
            queueClient.DequeueFaultAction = dequeueFaultAction;
            queueClient.HeartbeatFaultAction = heartbeatFaultAction;

            var definitions = new List<string>();
            var numberOfJobs = 50;
            for (var i = 0; i < numberOfJobs; ++i)
            {
                definitions.Add(i.ToString());
            }

            var jobs = await queueClient.EnqueueAsync(0, definitions.ToArray(), null, false, false, CancellationToken.None);
            Assert.Equal(numberOfJobs, jobs.Count);
            Assert.True(jobs.All(t => t.Status == JobStatus.Created));

            var jobHosting = new JobHosting(queueClient, factory, _logger);
            jobHosting.PollingFrequencyInSeconds = 0;
            jobHosting.MaxRunningJobCount = 10;
            jobHosting.JobHeartbeatIntervalInSeconds = 0.001;
            jobHosting.JobHeartbeatTimeoutThresholdInSeconds = 1;

            var tokenSource = new CancellationTokenSource();
            tokenSource.CancelAfter(TimeSpan.FromSeconds(60));
            var host = Task.Run(async () => await jobHosting.ExecuteAsync(0, "test", tokenSource));
            while (jobs.Where(t => t.Status == JobStatus.Completed).Count() < numberOfJobs && !tokenSource.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            tokenSource.Cancel();
            host.Wait();

            Assert.Equal(numberOfJobs, jobs.Where(t => t.Status == JobStatus.Completed).Count());
            Assert.True(completeErrorNumber > 5, $"completeErrorNumber={completeErrorNumber} > 5");
            Assert.True(dequeueErrorNumber > 5, $"dequeueErrorNumber={dequeueErrorNumber} > 5");
            Assert.True(heartbeatErrorNumber > 5, $"heartbeatErrorNumber={heartbeatErrorNumber} > 5");
        }
    }
}
