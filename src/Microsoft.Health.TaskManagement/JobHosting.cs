// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.Health.JobManagement
{
    public class JobHosting
    {
        private readonly IQueueClient _queueClient;
        private readonly IJobFactory _jobFactory;
        private readonly ILogger<JobHosting> _logger;

        public JobHosting(IQueueClient queueClient, IJobFactory jobFactory, ILogger<JobHosting> logger)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(jobFactory, nameof(jobFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _queueClient = queueClient;
            _jobFactory = jobFactory;
            _logger = logger;
        }

        public int PollingFrequencyInSeconds { get; set; } = Constants.DefaultPollingFrequencyInSeconds;

        public short MaxRunningJobCount { get; set; } = Constants.DefaultMaxRunningJobCount;

        public int JobHeartbeatTimeoutThresholdInSeconds { get; set; } = Constants.DefaultJobHeartbeatTimeoutThresholdInSeconds;

        public double JobHeartbeatIntervalInSeconds { get; set; } = Constants.DefaultJobHeartbeatIntervalInSeconds;

        [Obsolete("Temporary method to prevent build breaks within Health PaaS. Will be removed after code is updated in Health PaaS")]
        public async Task StartAsync(byte queueType, string workerName, CancellationTokenSource cancellationTokenSource, bool useHeavyHeartbeats = false)
        {
            await ExecuteAsync(queueType, workerName, cancellationTokenSource, useHeavyHeartbeats);
        }

        public async Task ExecuteAsync(byte queueType, string workerName, CancellationTokenSource cancellationTokenSource, bool useHeavyHeartbeats = false)
        {
            var workers = new List<Task>();

            // parallel dequeue
            for (var thread = 0; thread < MaxRunningJobCount; thread++)
            {
                workers.Add(Task.Run(async () =>
                {
                    // random delay to avoid convoys
                    await Task.Delay(TimeSpan.FromSeconds(RandomNumberGenerator.GetInt32(100) / 100.0 * PollingFrequencyInSeconds));

                    while (!cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        JobInfo nextJob = null;
                        if (_queueClient.IsInitialized())
                        {
                            try
                            {
                                nextJob = await _queueClient.DequeueAsync(queueType, workerName, JobHeartbeatTimeoutThresholdInSeconds, cancellationTokenSource.Token);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to dequeue new job.");
                            }
                        }

                        if (nextJob != null)
                        {
                            await ExecuteJobAsync(nextJob, useHeavyHeartbeats);
                        }
                        else
                        {
                            await Task.Delay(TimeSpan.FromSeconds(PollingFrequencyInSeconds), cancellationTokenSource.Token);
                        }
                    }
                }));
            }

            try
            {
                await Task.WhenAny(workers.ToArray()); // if any worker crashes exit
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job failed to execute");
            }
        }

        private async Task ExecuteJobAsync(JobInfo jobInfo, bool useHeavyHeartbeats)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            using var jobCancellationToken = new CancellationTokenSource();

            IJob job = _jobFactory.Create(jobInfo);

            if (job == null)
            {
                _logger.LogWarning("Not supported job type");
                return;
            }

            try
            {
                if (jobInfo.CancelRequested)
                {
                    // For cancelled job, try to execute it for potential cleanup.
                    jobCancellationToken.Cancel();
                }

                var progress = new Progress<string>((result) => { jobInfo.Result = result; });

#pragma warning disable CS0618 // Type or member is obsolete. Needed for Import jobs, we should move away from this method.
                var runningJob = useHeavyHeartbeats
                               ? ExecuteJobWithHeavyHeartbeatsAsync(
                                    _queueClient,
                                    jobInfo,
                                    cancellationSource => job.ExecuteAsync(jobInfo, progress, cancellationSource.Token),
                                    TimeSpan.FromSeconds(JobHeartbeatIntervalInSeconds),
                                    jobCancellationToken)
                               : ExecuteJobWithHeartbeatsAsync(
                                    _queueClient,
                                    jobInfo.QueueType,
                                    jobInfo.Id,
                                    jobInfo.Version,
                                    cancellationSource => job.ExecuteAsync(jobInfo, progress, cancellationSource.Token),
                                    TimeSpan.FromSeconds(JobHeartbeatIntervalInSeconds),
                                    jobCancellationToken);
#pragma warning restore CS0618 // Type or member is obsolete

                jobInfo.Result = await runningJob;
            }
            catch (RetriableJobException ex)
            {
                _logger.LogError(ex, "Job {JobId} failed with retriable exception.", jobInfo.Id);

                // Not complete the job for retriable exception.
                return;
            }
            catch (JobExecutionException ex)
            {
                _logger.LogError(ex, "Job {JobId} failed.", jobInfo.Id);
                jobInfo.Result = JsonConvert.SerializeObject(ex.Error);
                jobInfo.Status = JobStatus.Failed;

                try
                {
                    await _queueClient.CompleteJobAsync(jobInfo, ex.RequestCancellationOnFailure, CancellationToken.None);
                }
                catch (Exception completeEx)
                {
                    _logger.LogError(completeEx, "Job {JobId} failed to complete.", jobInfo.Id);
                }

                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed with generic exception.", jobInfo.Id);

                var error = new JobErrorInfo(ex.Message, ex.StackTrace);
                jobInfo.Result = JsonConvert.SerializeObject(error);
                jobInfo.Status = JobStatus.Failed;

                try
                {
                    await _queueClient.CompleteJobAsync(jobInfo, true, CancellationToken.None);
                }
                catch (Exception completeEx)
                {
                    _logger.LogError(completeEx, "Job {JobId} failed to complete.", jobInfo.Id);
                }

                return;
            }

            try
            {
                jobInfo.Status = JobStatus.Completed;
                await _queueClient.CompleteJobAsync(jobInfo, true, CancellationToken.None);
                _logger.LogInformation("Job {JobId} completed.", jobInfo.Id);
            }
            catch (Exception completeEx)
            {
                _logger.LogError(completeEx, "Job {JobId} failed to complete.", jobInfo.Id);
            }
        }

        public static async Task<string> ExecuteJobWithHeartbeatsAsync(IQueueClient queueClient, byte queueType, long jobId, long version, Func<CancellationTokenSource, Task<string>> action, TimeSpan heartbeatPeriod, CancellationTokenSource cancellationTokenSource)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(action, nameof(action));

            var jobInfo = new JobInfo { QueueType = queueType, Id = jobId, Version = version }; // not other data points

            await using (new Timer(async _ => await PutJobHeartbeatAsync(queueClient, jobInfo, cancellationTokenSource), null, TimeSpan.FromSeconds(RandomNumberGenerator.GetInt32(100) / 100.0 * heartbeatPeriod.TotalSeconds), heartbeatPeriod))
            {
                return await action(cancellationTokenSource);
            }
        }

        [Obsolete("Heartbeats should only update timestamp, results should only be written when job reaches terminal state.")]
        public static async Task<string> ExecuteJobWithHeavyHeartbeatsAsync(IQueueClient queueClient, JobInfo jobInfo, Func<CancellationTokenSource, Task<string>> action, TimeSpan heartbeatPeriod, CancellationTokenSource cancellationTokenSource)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            EnsureArg.IsNotNull(action, nameof(action));

            await using (new Timer(async _ => await PutJobHeartbeatAsync(queueClient, jobInfo, cancellationTokenSource), null, TimeSpan.FromSeconds(RandomNumberGenerator.GetInt32(100) / 100.0 * heartbeatPeriod.TotalSeconds), heartbeatPeriod))
            {
                return await action(cancellationTokenSource);
            }
        }

        private static async Task PutJobHeartbeatAsync(IQueueClient queueClient, JobInfo jobInfo, CancellationTokenSource cancellationTokenSource)
        {
            try // this try/catch is redundant with try/catch in queueClient.PutJobHeartbeatAsync, but it is extra guarantee
            {
                var cancel = await queueClient.PutJobHeartbeatAsync(jobInfo, cancellationTokenSource.Token);
                if (cancel)
                {
                    cancellationTokenSource.Cancel();
                }
            }
            catch
            {
                // do nothing
            }
        }
    }
}
