﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.CosmosDb.Features.Queries;
using Microsoft.Health.JobManagement;
using Polly;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Queues;

public class CosmosQueueClient : IQueueClient
{
    private readonly Func<IScoped<Container>> _containerFactory;
    private readonly ICosmosQueryFactory _queryFactory;
    private readonly ICosmosDbDistributedLockFactory _distributedLockFactory;
    private static readonly AsyncPolicy _retryPolicy = Policy
        .Handle<CosmosException>(ex => ex.StatusCode == HttpStatusCode.PreconditionFailed)
        .Or<CosmosException>(ex => ex.StatusCode == HttpStatusCode.TooManyRequests)
        .Or<RequestRateExceededException>()
        .WaitAndRetryAsync(5, _ => TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(100, 1000)));

    public CosmosQueueClient(
        Func<IScoped<Container>> containerFactory,
        ICosmosQueryFactory queryFactory,
        ICosmosDbDistributedLockFactory distributedLockFactory)
    {
        _containerFactory = EnsureArg.IsNotNull(containerFactory, nameof(containerFactory));
        _queryFactory = EnsureArg.IsNotNull(queryFactory, nameof(queryFactory));
        _distributedLockFactory = EnsureArg.IsNotNull(distributedLockFactory, nameof(distributedLockFactory));
    }

    public bool IsInitialized() => true;

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobInfo>> EnqueueAsync(
        byte queueType,
        string[] definitions,
        long? groupId,
        bool forceOneActiveJobGroup,
        bool isCompleted,
        CancellationToken cancellationToken)
    {
        EnsureArg.IsNotNull(definitions, nameof(definitions));

        var id = GetLongId();
        var jobInfos = new List<JobInfo>();

        using IScoped<Container> container = _containerFactory.Invoke();

        // Check if there are any existing jobs with the same definition
        var definitionHashes = definitions
            .Distinct()
            .ToDictionary(d => d.ComputeHash(), d => d);

        QueryDefinition existingJobsSpec = new QueryDefinition(@$"SELECT VALUE c FROM root c
             JOIN d in c.definitions
             WHERE c.queueType = @queueType 
              AND (c.groupId = @groupId OR @groupId = null)
              AND ARRAY_CONTAINS([0, 1], d.status)
              AND ARRAY_CONTAINS([{string.Join(",", definitionHashes.Select(x => $"'{x.Key}'"))}], d.definitionHash)")
            .WithParameter("@queueType", queueType)
            .WithParameter("@groupId", groupId);

        // Add existing job records to the list of job infos
        IReadOnlyList<JobGroupWrapper> existingJobs = await ExecuteQueryAsync(existingJobsSpec, null, queueType, cancellationToken);
        if (existingJobs.Count > 0)
        {
            IEnumerable<JobInfo> existingJobInfos =
                existingJobs.SelectMany(x => x.ToJobInfo(x.Definitions.Where(y => definitionHashes.ContainsKey(y.DefinitionHash))));

            jobInfos.AddRange(existingJobInfos);
        }

        // Filter new job definitions
        var existingJobHashes = existingJobs.SelectMany(x => x.Definitions).Select(x => x.DefinitionHash).ToHashSet();
        var newDefinitions = definitionHashes.Where(x => !existingJobHashes.Contains(x.Key)).Select(x => x.Value).ToArray();

        // If there are no new definitions, there is no need to create a new JobGroup record
        if (newDefinitions.Length == 0)
        {
            return jobInfos;
        }

        // If forceOneActiveJobGroup is true, then check if there are any existing active jobs with the same queue type
        if (forceOneActiveJobGroup)
        {
            await using ICosmosDbDistributedLock distributedLock = _distributedLockFactory.Create(container.Value, "__jobQueue:" + queueType);

            if (await distributedLock.TryAcquireLock())
            {
                QueryDefinition sqlQuerySpec = new QueryDefinition(@"SELECT VALUE count(1) FROM root c JOIN d in c.definitions
         WHERE c.queueType = @queueType 
           AND ARRAY_CONTAINS([0, 1], d.status)").WithParameter("@queueType", queueType);

                var query = _queryFactory.Create<int>(
                    container.Value,
                    new CosmosQueryContext(
                        sqlQuerySpec,
                        new QueryRequestOptions { PartitionKey = new PartitionKey(JobGroupWrapper.GetJobInfoPartitionKey(queueType)) }));

                FeedResponse<int> itemResponse = await query.ExecuteNextAsync(cancellationToken);

                if (itemResponse.Resource.FirstOrDefault() > 0)
                {
                    throw new JobConflictException("Failed to enqueue job.");
                }

                jobInfos.AddRange(await CreateNewJob(id, queueType, newDefinitions, groupId, isCompleted, cancellationToken));
            }
        }
        else
        {
            jobInfos.AddRange(await CreateNewJob(id, queueType, newDefinitions, groupId, isCompleted, cancellationToken));
        }

        return jobInfos;
    }

    private async Task<IReadOnlyList<JobInfo>> CreateNewJob(long id, byte queueType, string[] definitions, long? groupId, bool isCompleted, CancellationToken cancellationToken)
    {
        var jobInfo = new JobGroupWrapper
        {
            Id = id.ToString(),
            QueueType = queueType,
            GroupId = groupId?.ToString() ?? id.ToString(),
            CreateDate = Clock.UtcNow,
            TimeToLive = (int)TimeSpan.FromDays(30).TotalSeconds,
        };

        // The first JobId is the same as the JobGroupWrapper.Id and is sequentially incremented
        var jobId = id;

        foreach (var item in definitions)
        {
            var definitionInfo = new JobDefinitionWrapper
            {
                JobId = (jobId++).ToString(),
                Status = isCompleted ? (byte)JobStatus.Completed : (byte)JobStatus.Created,
                Definition = item,
                DefinitionHash = item.ComputeHash(),
            };

            jobInfo.Definitions.Add(definitionInfo);
        }

        using IScoped<Container> container = _containerFactory.Invoke();
        ItemResponse<JobGroupWrapper> result = await _retryPolicy.ExecuteAsync(async () => await container.Value.CreateItemAsync(jobInfo, new PartitionKey(jobInfo.PartitionKey), cancellationToken: cancellationToken));

        return result.Resource.ToJobInfo().ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<JobInfo>> DequeueJobsAsync(
        byte queueType,
        int numberOfJobsToDequeue,
        string worker,
        int heartbeatTimeoutSec,
        CancellationToken cancellationToken)
    {
        QueryDefinition sqlQuerySpec = new QueryDefinition(@"SELECT VALUE c FROM root c
           JOIN d in c.definitions
           WHERE c.queueType = @queueType 
           AND (d.status = 0 OR
               (d.status = 1 AND d.heartbeatDateTime < @heartbeatDateTimeout))
           ORDER BY c.priority ASC")
            .WithParameter("@queueType", queueType)
            .WithParameter("@heartbeatDateTimeout", Clock.UtcNow.AddSeconds(-heartbeatTimeoutSec));

        var dequeuedJobs = new List<JobInfo>();

        return await _retryPolicy.ExecuteAsync(async () =>
            {
                IReadOnlyList<JobGroupWrapper> response = await ExecuteQueryAsync(sqlQuerySpec, numberOfJobsToDequeue, queueType, cancellationToken);

                foreach (JobGroupWrapper item in response)
                {
                    // JobGroupWrapper can convert to multiple JobsInfos.
                    // The collection is outside the scope of the retry since once we've saved that JobGroupWrapper we have successfully dequeued those job.
                    // This loop is to check additional JobGroupWrappers if we're trying find more jobs up to 'numberOfJobsToDequeue'.
                    IReadOnlyCollection<JobInfo> dequeued = await DequeueItemsInternalAsync(item, numberOfJobsToDequeue - dequeuedJobs.Count, worker, heartbeatTimeoutSec, cancellationToken);
                    dequeuedJobs.AddRange(dequeued);

                    if (numberOfJobsToDequeue - dequeuedJobs.Count <= 0)
                    {
                        break;
                    }
                }

                return dequeuedJobs;
            });
    }

    private async Task<IReadOnlyCollection<JobInfo>> DequeueItemsInternalAsync(JobGroupWrapper item, int numberOfJobsToDequeue, string worker, int heartbeatTimeoutSec, CancellationToken cancellationToken, string jobId = null)
    {
        // Filter Job Definitions needing to be dequeued
        var scan = item.Definitions
            .Where(x => x.JobId == jobId
                 || (jobId == null &&
                     (x.Status == (byte)JobStatus.Created
                     || (x.Status == (byte)JobStatus.Running && x.HeartbeatDateTime < Clock.UtcNow.AddSeconds(-heartbeatTimeoutSec)))))
            .OrderBy(x => x.Status)
            .ToList();

        if (scan.Count == 0)
        {
            return Array.Empty<JobInfo>();
        }

        var toReturn = new List<JobDefinitionWrapper>();
        foreach (JobDefinitionWrapper job in scan.Take(numberOfJobsToDequeue))
        {
            if (!string.IsNullOrEmpty(job.Worker))
            {
                job.Info = $"Prev worker: {job.Worker}. ";
            }

            if (job.DequeueCount > 10)
            {
                if (job.Status == (byte)JobStatus.Running)
                {
                    job.CancelRequested = true;
                }

                job.Status = (byte)JobStatus.Failed;
                job.Info += "Dequeue count exceeded.";
            }
            else
            {
                job.Status = (byte)JobStatus.Running;
                job.HeartbeatDateTime = Clock.UtcNow;
                job.Worker = worker;
                job.DequeueCount += 1;
                job.StartDate ??= Clock.UtcNow;

                // Job Version is used to detect if the job has been updated while it was running (used by Heartbeat)
                job.Version = GenerateVersion();

                toReturn.Add(job);
            }
        }

        await SaveJobGroupAsync(item, cancellationToken);

        return item.ToJobInfo(toReturn).ToList();
    }

    /// <inheritdoc />
    public async Task<JobInfo> DequeueAsync(byte queueType, string worker, int heartbeatTimeoutSec, CancellationToken cancellationToken, long? jobId = null, bool checkTimeoutJobsOnly = false)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            if (jobId == null)
            {
                var job = await DequeueJobsAsync(queueType, 1, worker, heartbeatTimeoutSec, cancellationToken);
                return job?.FirstOrDefault();
            }

            var jobs = await GetJobsByIdsInternalAsync(queueType, new[] { jobId.Value }, false, cancellationToken);
            if (jobs.Count == 1)
            {
                var job = await DequeueItemsInternalAsync(jobs[0].JobGroup, 1, worker, heartbeatTimeoutSec, cancellationToken);
                return job?.FirstOrDefault();
            }

            throw new JobNotExistException("Job not found.");
        });
    }

    /// <inheritdoc />
    public async Task<JobInfo> GetJobByIdAsync(byte queueType, long jobId, bool returnDefinition, CancellationToken cancellationToken)
    {
        IReadOnlyList<JobInfo> job = await GetJobsByIdsAsync(queueType, new[] { jobId }, returnDefinition, cancellationToken);

        if (job.Count == 1)
        {
            return job[0];
        }

        if (job.Count > 1)
        {
            throw new InvalidOperationException("More than one job found.");
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobInfo>> GetJobsByIdsAsync(byte queueType, long[] jobIds, bool returnDefinition, CancellationToken cancellationToken)
    {
        var jobs = await GetJobsByIdsInternalAsync(queueType, jobIds, returnDefinition, cancellationToken);

        var infos = new List<JobInfo>();
        foreach ((JobGroupWrapper JobGroup, IReadOnlyList<JobDefinitionWrapper> MatchingJob) item in jobs)
        {
            infos.AddRange(item.JobGroup.ToJobInfo(item.MatchingJob));
        }

        return infos;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobInfo>> GetJobByGroupIdAsync(byte queueType, long groupId, bool returnDefinition, CancellationToken cancellationToken)
    {
        IReadOnlyList<JobGroupWrapper> jobs = await GetGroupInternalAsync(queueType, groupId, cancellationToken);
        return jobs.SelectMany(job => job.ToJobInfo(job.Definitions)).ToList();
    }

    /// <inheritdoc />
    public async Task<bool> PutJobHeartbeatAsync(JobInfo jobInfo, CancellationToken cancellationToken)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var jobs = await GetJobsByIdsInternalAsync(jobInfo.QueueType, new[] { jobInfo.Id }, false, cancellationToken);

            var job = jobs.Single();
            JobDefinitionWrapper item = job.MatchingJob.Single();

            if (item.Version != jobInfo.Version)
            {
                throw new JobConflictException("Job version mismatch.");
            }

            if (item.Status == (byte)JobStatus.Running)
            {
                item.HeartbeatDateTime = Clock.UtcNow;

                if (jobInfo.Data.HasValue)
                {
                    item.Data = jobInfo.Data;
                }

                if (!string.IsNullOrEmpty(jobInfo.Result))
                {
                    item.Result = jobInfo.Result;
                }

                await SaveJobGroupAsync(job.JobGroup, cancellationToken);
            }

            return item.CancelRequested;
        });
    }

    /// <inheritdoc />
    public async Task CancelJobByGroupIdAsync(byte queueType, long groupId, CancellationToken cancellationToken)
    {
        IReadOnlyList<JobGroupWrapper> jobs = default;
        var cancelTasks = new List<Task>();

        await _retryPolicy.ExecuteAsync(async () =>
        {
            jobs = await GetGroupInternalAsync(queueType, groupId, cancellationToken);
        });

        foreach (JobGroupWrapper job in jobs)
        {
            bool saveRequired = false;

            foreach (JobDefinitionWrapper item in job.Definitions)
            {
                if (item.Status == (byte)JobStatus.Running)
                {
                    item.CancelRequested = true;
                    saveRequired = true;
                }
                else if (item.Status == (byte)JobStatus.Created)
                {
                    item.Status = (byte)JobStatus.Cancelled;
                    item.CancelRequested = true;
                    saveRequired = true;
                }
            }

            if (saveRequired)
            {
                cancelTasks.Add(Task.Run(
                    async () =>
                    {
                        await _retryPolicy.ExecuteAsync(async () =>
                        {
                            await SaveJobGroupAsync(job, cancellationToken, ignoreEtag: true);
                        });
                    },
                    cancellationToken));
            }
        }

        await Task.WhenAll(cancelTasks);
    }

    /// <inheritdoc />
    public async Task CancelJobByIdAsync(byte queueType, long jobId, CancellationToken cancellationToken)
    {
        await _retryPolicy.ExecuteAsync(async () =>
        {
            var jobs = await GetJobsByIdsInternalAsync(queueType, new[] { jobId }, false, cancellationToken);

            if (jobs.Count == 1)
            {
                (JobGroupWrapper JobGroup, IReadOnlyList<JobDefinitionWrapper> MatchingJob) job = jobs[0];
                JobDefinitionWrapper item = job.MatchingJob[0];

                if (item != null)
                {
                    CancelJobDefinition(item);

                    await SaveJobGroupAsync(job.JobGroup, cancellationToken, ignoreEtag: true);
                }
            }
        });
    }

    /// <inheritdoc />
    public async Task CompleteJobAsync(JobInfo jobInfo, bool requestCancellationOnFailure, CancellationToken cancellationToken)
    {
        await _retryPolicy.ExecuteAsync(async () =>
        {
            var jobs = await GetJobsByIdsInternalAsync(jobInfo.QueueType, new[] { jobInfo.Id }, false, cancellationToken);

            (JobGroupWrapper JobGroup, IReadOnlyList<JobDefinitionWrapper> MatchingJob) definitionTuple = jobs.Single();
            JobDefinitionWrapper item = definitionTuple.MatchingJob.Single();

            if (jobInfo.Status == JobStatus.Failed)
            {
                // If a job fails with requestCancellationOnFailure, all other jobs in the group should be cancelled.
                if (requestCancellationOnFailure)
                {
                    foreach (JobDefinitionWrapper jobDefinition in definitionTuple.JobGroup.Definitions)
                    {
                        CancelJobDefinition(jobDefinition);
                    }
                }

                item.Status = (byte)JobStatus.Failed;
            }
            else if (item.CancelRequested)
            {
                item.Status = (byte)JobStatus.Cancelled;
            }
            else
            {
                item.Status = (byte?)JobStatus.Completed;
            }

            item.EndDate = Clock.UtcNow;
            item.Result = jobInfo.Result;

            await SaveJobGroupAsync(definitionTuple.JobGroup, cancellationToken);
        });
    }

    private async Task<IReadOnlyList<JobGroupWrapper>> GetGroupInternalAsync(
        byte queueType,
        long groupId,
        CancellationToken cancellationToken)
    {
            QueryDefinition sqlQuerySpec = new QueryDefinition(@"SELECT VALUE c FROM root c
           WHERE c.groupId = @groupId and c.queueType = @queueType")
                .WithParameter("@groupId", groupId.ToString())
                .WithParameter("@queueType", queueType);

            var response = await ExecuteQueryAsync(sqlQuerySpec, null, queueType, cancellationToken);

            return response.ToList();
    }

    private async Task<IReadOnlyList<(JobGroupWrapper JobGroup, IReadOnlyList<JobDefinitionWrapper> MatchingJob)>> GetJobsByIdsInternalAsync(byte queueType, long[] jobIds, bool returnDefinition, CancellationToken cancellationToken)
    {
        QueryDefinition sqlQuerySpec = new QueryDefinition(@$"SELECT VALUE c FROM root c JOIN d in c.definitions
           WHERE c.queueType = @queueType 
           AND ARRAY_CONTAINS([{string.Join(",", jobIds.Select(x => $"'{x}'"))}], d.jobId)")
            .WithParameter("@queueType", queueType);

        var response = await ExecuteQueryAsync(sqlQuerySpec, jobIds.Length, queueType, cancellationToken);

        var infos = new List<(JobGroupWrapper JobGroup, IReadOnlyList<JobDefinitionWrapper> MatchingJob)>();
        var jobIdsString = jobIds.Select(x => x.ToString()).ToHashSet();

        foreach (JobGroupWrapper item in response)
        {
            var scan = item.Definitions
                .Where(x => jobIdsString.Contains(x.JobId))
                .ToList();

            infos.Add((item, scan));
        }

        return infos;
    }

    private static void CancelJobDefinition(JobDefinitionWrapper item)
    {
        switch (item.Status)
        {
            case (byte)JobStatus.Running:
                item.CancelRequested = true;
                break;
            case (byte)JobStatus.Created:
                item.Status = (byte)JobStatus.Cancelled;
                item.EndDate = Clock.UtcNow;
                break;
        }
    }

    private async Task<IReadOnlyList<JobGroupWrapper>> ExecuteQueryAsync(QueryDefinition sqlQuerySpec, int? itemCount, byte queueType, CancellationToken cancellationToken)
    {
        IScoped<Container> container = null;

        try
        {
            container = _containerFactory.Invoke();
        }
        catch (ObjectDisposedException ode)
        {
            throw new ServiceUnavailableException(Resources.NotAbleToExecuteQuery, ode);
        }

        using (container)
        {
            ICosmosQuery<JobGroupWrapper> query = _queryFactory.Create<JobGroupWrapper>(
                container.Value,
                new CosmosQueryContext(
                    sqlQuerySpec,
                    new QueryRequestOptions { PartitionKey = new PartitionKey(JobGroupWrapper.GetJobInfoPartitionKey(queueType)), MaxItemCount = itemCount }));

            var items = new List<JobGroupWrapper>();
            FeedResponse<JobGroupWrapper> response;

            while (itemCount == null || items.Count < itemCount.Value)
            {
                response = await _retryPolicy.ExecuteAsync(async () => await query.ExecuteNextAsync(cancellationToken));
                items.AddRange(response);

                if (string.IsNullOrEmpty(response.ContinuationToken))
                {
                    break;
                }
            }

            return items;
        }
    }

    private async Task SaveJobGroupAsync(JobGroupWrapper definition, CancellationToken cancellationToken, bool ignoreEtag = false)
    {
        using IScoped<Container> container = _containerFactory.Invoke();

        try
        {
            await _retryPolicy.ExecuteAsync(async () =>
                await container.Value.UpsertItemAsync(
                    definition,
                    new PartitionKey(definition.PartitionKey),
                    ignoreEtag ? new() : new() { IfMatchEtag = definition.ETag },
                    cancellationToken: cancellationToken));
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.RequestEntityTooLarge)
        {
            throw new JobExecutionException("Job data too large.", ex);
        }
    }

    private static long GetLongId()
    {
        return Clock.UtcNow.DateTime.DateToId() + RandomNumberGenerator.GetInt32(100, 999);
    }

    /// <summary>
    /// Returns a version number based on the current time.
    /// Similar to SQL "datediff_big(millisecond,'0001-01-01',getUTCdate())"
    /// </summary>
    private static long GenerateVersion()
    {
        TimeSpan diff = DateTime.UtcNow - new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return (long)diff.TotalMilliseconds;
    }
}
