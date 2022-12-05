// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        private readonly ConcurrentDictionary<long, Func<Task>> _activeJobsNeedKeepAlive = new();

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

        public async Task StartAsync(byte queueType, string workerName, CancellationTokenSource cancellationToken)
        {
            using var keepAliveCancellationToken = new CancellationTokenSource();
            Task keepAliveTask = KeepAliveJobsAsync(keepAliveCancellationToken.Token);

            await PullAndProcessJobsAsync(queueType, workerName, cancellationToken.Token);

            keepAliveCancellationToken.Cancel();
            await keepAliveTask;
        }

        private async Task PullAndProcessJobsAsync(byte queueType, string workerName, CancellationToken cancellationToken)
        {
            var runningJobs = new List<Task>();

            while (!cancellationToken.IsCancellationRequested)
            {
                var intervalDelayTask = Task.Delay(TimeSpan.FromSeconds(PollingFrequencyInSeconds), CancellationToken.None);

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
                        nextJob = await _queueClient.DequeueAsync(queueType, workerName, JobHeartbeatTimeoutThresholdInSeconds, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to pull new jobs.");
                    }
                }

                if (nextJob != null)
                {
                    runningJobs.Add(ExecuteJobAsync(nextJob));
                }
                else
                {
                    await intervalDelayTask;
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

        private async Task ExecuteJobAsync(JobInfo jobInfo)
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
                try
                {
                    if (jobInfo.CancelRequested && !jobCancellationToken.IsCancellationRequested)
                    {
                        // For cancelled job, try to execute it for potential cleanup.
                        jobCancellationToken.Cancel();
                    }

                    var progress = new Progress<string>((result) =>
                    {
                        jobInfo.Result = result;
                    });

                    var runningJob = Task.Run(() => job.ExecuteAsync(jobInfo, progress, jobCancellationToken.Token));
                    _activeJobsNeedKeepAlive[jobInfo.Id] = () => KeepAliveSingleJobAsync(jobInfo, jobCancellationToken);

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

                    object error = new { message = ex.Message, stackTrace = ex.StackTrace };
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
            finally
            {
                _activeJobsNeedKeepAlive.Remove(jobInfo.Id, out _);
            }
        }

        private async Task KeepAliveSingleJobAsync(JobInfo jobInfo, CancellationTokenSource jobCancellationToken)
        {
            try
            {
                bool shouldCancel = false;
                try
                {
                    bool cancelRequested = await _queueClient.KeepAliveJobAsync(jobInfo, CancellationToken.None);
                    shouldCancel |= cancelRequested;
                }
                catch (JobNotExistException notExistEx)
                {
                    _logger.LogError(notExistEx, "Job {JobId} not exist or {RunId} not match.", jobInfo.Id, jobInfo.Version);
                    shouldCancel = true;
                }

                if (shouldCancel && !jobCancellationToken.IsCancellationRequested)
                {
                    jobCancellationToken.Cancel();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to keep alive on job {JobId}", jobInfo.Id);
            }
        }

        private async Task KeepAliveJobsAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Start to keep alive job message.");

            while (!cancellationToken.IsCancellationRequested)
            {
                var intervalDelayTask = Task.Delay(TimeSpan.FromSeconds(JobHeartbeatIntervalInSeconds), CancellationToken.None);
                KeyValuePair<long, Func<Task>>[] activeJobRecords = _activeJobsNeedKeepAlive.ToArray();

                foreach ((long jobId, Func<Task> keepAliveFunc) in activeJobRecords)
                {
                    try
                    {
                        await keepAliveFunc();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to keep alive on job {JobId}", jobId);
                    }
                }

                await intervalDelayTask;
            }

            _logger.LogInformation("Stop to keep alive job message.");
        }
    }
}
