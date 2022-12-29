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

        public int JobHeartbeatIntervalInSeconds { get; set; } = Constants.DefaultJobHeartbeatIntervalInSeconds;

        public async Task ExecuteAsync(byte queueType, string workerName, CancellationTokenSource cancellationTokenSource, bool useHeavyHeartbeats = false)
        {
            var runningJobs = new List<Task>();

            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                if (runningJobs.Count >= MaxRunningJobCount)
                {
                    _ = await Task.WhenAny(runningJobs.ToArray());
                    runningJobs.RemoveAll(t => t.IsCompleted);
                }

                JobInfo nextJob = null;
                if (_queueClient.IsInitialized())
                {
                    try
                    {
                        nextJob = await _queueClient.DequeueAsync(queueType, workerName, JobHeartbeatTimeoutThresholdInSeconds, cancellationTokenSource.Token);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to pull new jobs.");
                    }
                }

                if (nextJob != null)
                {
                    runningJobs.Add(ExecuteJobAsync(nextJob, useHeavyHeartbeats));
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(PollingFrequencyInSeconds), CancellationToken.None);
                }
            }

            try
            {
                await Task.WhenAll(runningJobs.ToArray());
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
                _logger.LogError(ex, "Job {JobId} failed.", jobInfo.Id);

                object error = new { message = ex.Message };
                jobInfo.Result = JsonConvert.SerializeObject(error);
                jobInfo.Status = JobStatus.Failed;

                try
                {
                    await _queueClient.CompleteJobAsync(jobInfo, false, CancellationToken.None);
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

            await using (new Timer(async _ => await PutJobHeartbeat(queueClient, queueType, jobId, version, cancellationTokenSource), null, TimeSpan.FromSeconds(RandomNumberGenerator.GetInt32(100) / 100.0 * heartbeatPeriod.TotalSeconds), heartbeatPeriod))
            {
                return await action(cancellationTokenSource);
            }
        }

        public static async Task<string> ExecuteJobWithHeavyHeartbeatsAsync(IQueueClient queueClient, JobInfo jobInfo, Func<CancellationTokenSource, Task<string>> action, TimeSpan heartbeatPeriod, CancellationTokenSource cancellationTokenSource)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            EnsureArg.IsNotNull(action, nameof(action));

            await using (new Timer(async _ => await PutJobHeartbeatHeavy(queueClient, jobInfo, cancellationTokenSource), null, TimeSpan.FromSeconds(RandomNumberGenerator.GetInt32(100) / 100.0 * heartbeatPeriod.TotalSeconds), heartbeatPeriod))
            {
                return await action(cancellationTokenSource);
            }
        }

        private static async Task PutJobHeartbeat(IQueueClient queueClient, byte queueType, long jobId, long version, CancellationTokenSource cancellationTokenSource)
        {
            var cancel = await queueClient.PutJobHeartbeatAsync(new JobInfo { QueueType = queueType, Id = jobId, Version = version }, cancellationTokenSource.Token);
            if (cancel)
            {
                cancellationTokenSource.Cancel();
            }
        }

        private static async Task PutJobHeartbeatHeavy(IQueueClient queueClient, JobInfo jobInfo, CancellationTokenSource cancellationTokenSource)
        {
            var cancel = await queueClient.PutJobHeartbeatAsync(jobInfo, cancellationTokenSource.Token);
            if (cancel)
            {
                cancellationTokenSource.Cancel();
            }
        }
    }
}
