// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Microsoft.Health.JobManagement
{
    public class JobHosting
    {
        private static readonly ActivitySource _jobHostingActivitySource = new ActivitySource(nameof(JobHosting));
        private readonly IQueueClient _queueClient;
        private readonly IJobFactory _jobFactory;
        private readonly ILogger<JobHosting> _logger;
        private DateTime _lastHeartbeatLog;

        public JobHosting(IQueueClient queueClient, IJobFactory jobFactory, ILogger<JobHosting> logger)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(jobFactory, nameof(jobFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _queueClient = queueClient;
            _jobFactory = jobFactory;
            _logger = logger;
            RunningJobsTarget = new ConcurrentDictionary<byte, int>();
        }

        public int PollingFrequencyInSeconds { get; set; } = Constants.DefaultPollingFrequencyInSeconds;

        public int JobHeartbeatTimeoutThresholdInSeconds { get; set; } = Constants.DefaultJobHeartbeatTimeoutThresholdInSeconds;

        public double JobHeartbeatIntervalInSeconds { get; set; } = Constants.DefaultJobHeartbeatIntervalInSeconds;

        public ConcurrentDictionary<byte, int> RunningJobsTarget { get; private set; }

        public async Task ExecuteAsync(byte queueType, short runningJobCount, string workerName, CancellationTokenSource cancellationTokenSource)
        {
            _logger.LogInformation("Queue={QueueType}: job hosting is starting...", queueType);
            SetRunningJobsTarget(queueType, runningJobCount); // this happens only once according to our current logic
            _lastHeartbeatLog = DateTime.UtcNow;
            var workers = new List<Task<JobInfo>>();
            var dequeueDelay = true;
            var dequeueTimeoutJobsStopwatch = Stopwatch.StartNew();
            var dequeueTimeoutJobsCounter = 0;
            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                RunningJobsTarget.TryGetValue(queueType, out var runningJobsTarget);
                while (workers.Count < runningJobsTarget && !cancellationTokenSource.Token.IsCancellationRequested)
                {
                    workers.Add(Task.Run(async () =>
                    {
                        //// wait
                        if (dequeueDelay)
                        {
                            var delay = TimeSpan.FromSeconds(((RandomNumberGenerator.GetInt32(20) / 100.0) + 0.9) * PollingFrequencyInSeconds); // random delay to avoid convoys
                            _logger.LogDebug("Queue={QueueType}: delaying job execution for {DequeueDelay}.", queueType, delay);
                            await Task.Delay(delay);
                        }

                        JobInfo job = null;
                        //// dequeue
                        if (_queueClient.IsInitialized())
                        {
                            try
                            {
                                _logger.LogDebug("Queue={QueueType}: dequeuing next job...", queueType);

                                if (Interlocked.Decrement(ref dequeueTimeoutJobsCounter) >= 0)
                                {
                                    job = await _queueClient.DequeueAsync(queueType, workerName, JobHeartbeatTimeoutThresholdInSeconds, cancellationTokenSource.Token, null, true);
                                }

                                job ??= await _queueClient.DequeueAsync(queueType, workerName, JobHeartbeatTimeoutThresholdInSeconds, cancellationTokenSource.Token);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Queue={QueueType}: failed to dequeue new job.", queueType);
                            }
                        }

                        //// execute
                        if (job != null)
                        {
                            await ExecuteJobWithActivityAsync(job);
                        }

                        return job;
                    }));

                    _logger.LogDebug("Queue={QueueType}: total workers = {Workers}.", queueType, workers.Count);
                }

                try
                {
                    var completed = await Task.WhenAny(workers);
                    workers.Remove(completed);
                    dequeueDelay = await completed == null; // no job info == queue was empty
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Queue={QueueType}: job hosting task failed.", queueType);
                    await cancellationTokenSource.CancelAsync();
                }

                if (dequeueTimeoutJobsStopwatch.Elapsed.TotalSeconds > 600)
                {
                    dequeueTimeoutJobsCounter = runningJobsTarget;
                    dequeueTimeoutJobsStopwatch.Restart();
                }

                if (DateTime.UtcNow - _lastHeartbeatLog > TimeSpan.FromHours(1))
                {
                    _lastHeartbeatLog = DateTime.UtcNow;
                    _logger.LogInformation("Queue={QueueType}: job hosting is running, total workers = {Workers}.", queueType, workers.Count);
                }
            }

            try
            {
                await Task.WhenAll(workers.ToArray());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Queue={QueueType}: job hosting task failed.", queueType);
            }
        }

        public void SetRunningJobsTarget(byte queueType, int target)
        {
            if (!RunningJobsTarget.ContainsKey(queueType))
            {
                RunningJobsTarget.TryAdd(queueType, target);
            }
            else
            {
                RunningJobsTarget[queueType] = target;
            }

            _logger.LogInformation("Queue={QueueType}: running jobs target is set to {RunningJobsTarget}.", queueType, target);
        }

        private async Task ExecuteJobWithActivityAsync(JobInfo nextJob)
        {
            using (Activity activity = _jobHostingActivitySource.StartActivity(_jobHostingActivitySource.Name, ActivityKind.Server))
            {
                if (activity == null)
                {
                    _logger.LogWarning("Failed to start an activity.");
                }

                activity?.SetTag("CreateDate", nextJob.CreateDate);
                activity?.SetTag("HeartbeatDateTime", nextJob.HeartbeatDateTime);
                activity?.SetTag("Id", nextJob.Id);
                activity?.SetTag("QueueType", nextJob.QueueType);
                activity?.SetTag("Version", nextJob.Version);

                _logger.LogJobInformation(nextJob, "Job dequeued.");
                await ExecuteJobAsync(nextJob);
            }
        }

        private async Task ExecuteJobAsync(JobInfo jobInfo)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            using var jobCancellationToken = new CancellationTokenSource();

            using IScoped<IJob> job = _jobFactory.Create(jobInfo);

            if (job?.Value == null)
            {
                _logger.LogJobWarning(jobInfo, "Not supported job type.", jobInfo.Id);
                return;
            }

            try
            {
                _logger.LogJobInformation(jobInfo, "Job starting.", jobInfo.Id, jobInfo.QueueType);

                if (jobInfo.CancelRequested)
                {
                    // For cancelled job, try to execute it for potential cleanup.
                    await jobCancellationToken.CancelAsync();
                }

                Task<string> runningJob = ExecuteJobWithHeartbeatsAsync(
                                    _queueClient,
                                    jobInfo.QueueType,
                                    jobInfo.Id,
                                    jobInfo.Version,
                                    cancellationSource => job.Value.ExecuteAsync(jobInfo, cancellationSource.Token),
                                    TimeSpan.FromSeconds(JobHeartbeatIntervalInSeconds),
                                    jobCancellationToken);

                jobInfo.Result = await runningJob;
            }
            catch (JobExecutionException ex)
            {
                if (ex.IsCustomerCaused)
                {
                    _logger.LogJobWarning(ex, jobInfo, "Job failed due to customer caused issue.", jobInfo.Id, jobInfo.GroupId, jobInfo.QueueType);
                }
                else
                {
                    _logger.LogJobError(ex, jobInfo, "Job failed.", jobInfo.Id, jobInfo.GroupId, jobInfo.QueueType);
                }

                jobInfo.Result = JsonConvert.SerializeObject(ex.Error);
                jobInfo.Status = JobStatus.Failed;

                try
                {
                    await _queueClient.CompleteJobAsync(jobInfo, true, CancellationToken.None);
                }
                catch (Exception completeEx)
                {
                    _logger.LogJobError(completeEx, jobInfo, "Job failed to complete.", jobInfo.Id, jobInfo.GroupId, jobInfo.QueueType);
                }

                return;
            }
            catch (JobExecutionSoftFailureException ex)
            {
                if (ex.IsCustomerCaused)
                {
                    _logger.LogJobWarning(ex, jobInfo, "Job soft failed due to customer caused issue.", jobInfo.Id, jobInfo.GroupId, jobInfo.QueueType);
                }
                else
                {
                    _logger.LogJobError(ex, jobInfo, "Job soft failed.", jobInfo.Id, jobInfo.GroupId, jobInfo.QueueType);
                }

                jobInfo.Result = JsonConvert.SerializeObject(ex.Error);
                jobInfo.Status = JobStatus.Failed;

                try
                {
                    await _queueClient.CompleteJobAsync(jobInfo, false, CancellationToken.None);
                }
                catch (Exception completeEx)
                {
                    _logger.LogJobError(completeEx, jobInfo, "Job failed to complete on soft job failure.", jobInfo.Id, jobInfo.GroupId, jobInfo.QueueType);
                }

                return;
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogJobWarning(ex, jobInfo, "Job canceled due to unhandled cancellation exception.", jobInfo.Id, jobInfo.GroupId, jobInfo.QueueType);
                jobInfo.Status = JobStatus.Cancelled;

                try
                {
                    await _queueClient.CompleteJobAsync(jobInfo, true, CancellationToken.None);
                }
                catch (Exception completeEx)
                {
                    _logger.LogJobError(completeEx, jobInfo, "Job failed to complete.", jobInfo.Id, jobInfo.GroupId, jobInfo.QueueType);
                }

                return;
            }
            catch (Exception ex)
            {
                _logger.LogJobError(ex, jobInfo, "Job failed with generic exception.", jobInfo.Id, jobInfo.GroupId, jobInfo.QueueType);

                object error = new { message = ex.Message, stackTrace = ex.StackTrace };
                jobInfo.Result = JsonConvert.SerializeObject(error);
                jobInfo.Status = JobStatus.Failed;

                try
                {
                    await _queueClient.CompleteJobAsync(jobInfo, true, CancellationToken.None);
                }
                catch (Exception completeEx)
                {
                    _logger.LogJobError(completeEx, jobInfo, "Job failed to complete.", jobInfo.Id, jobInfo.GroupId, jobInfo.QueueType);
                }

                return;
            }

            try
            {
                jobInfo.Status = JobStatus.Completed;
                await _queueClient.CompleteJobAsync(jobInfo, true, CancellationToken.None);
                _logger.LogJobInformation(jobInfo, "Job completed.", jobInfo.Id, jobInfo.GroupId, jobInfo.QueueType);
            }
            catch (Exception completeEx)
            {
                _logger.LogJobError(completeEx, jobInfo, "Job failed to complete.", jobInfo.Id, jobInfo.GroupId, jobInfo.QueueType);
            }
        }

        public static async Task<string> ExecuteJobWithHeartbeatsAsync(IQueueClient queueClient, byte queueType, long jobId, long version, Func<CancellationTokenSource, Task<string>> action, TimeSpan heartbeatPeriod, CancellationTokenSource cancellationTokenSource)
        {
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(action, nameof(action));

            var jobInfo = new JobInfo { QueueType = queueType, Id = jobId, Version = version }; // not other data points

            // WARNING: Avoid using 'async' lambda when delegate type returns 'void'
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
                    await cancellationTokenSource.CancelAsync();
                }
            }
            catch
            {
                // do nothing
            }
        }
    }
}
