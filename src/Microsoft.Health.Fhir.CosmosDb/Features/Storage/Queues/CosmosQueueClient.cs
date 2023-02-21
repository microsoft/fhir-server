// -------------------------------------------------------------------------------------------------
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
using Microsoft.Health.Core;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Extensions.DependencyInjection;
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
        .Handle<RetriableJobException>()
        .WaitAndRetryAsync(3, _ => TimeSpan.FromMilliseconds(GenerateRandomNumber()));

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

        var definitionHashes = definitions.Select(d => $"'{d.ComputeHash()}'").ToList();

        using IScoped<Container> container = _containerFactory.Invoke();

        QueryDefinition existingJobsSpec = new QueryDefinition(@$"SELECT VALUE c FROM root c
             JOIN d in c.definitions
             WHERE c.queueType = @queueType 
              AND (c.groupId = @groupId OR @groupId = null)
              AND ARRAY_CONTAINS([0, 1], d.status)
              AND ARRAY_CONTAINS([{string.Join(",", definitionHashes)}], d.definitionHash)")
            .WithParameter("@queueType", queueType)
            .WithParameter("@groupId", groupId);

        FeedResponse<JobGroupWrapper> existingJobs = await ExecuteQueryAsync(existingJobsSpec, 100, cancellationToken);

        if (existingJobs.Count > 0)
        {
            IEnumerable<JobInfo> existingJobInfos =
                existingJobs.Resource
                    .SelectMany(x => x.ToJobInfo(x.Definitions.Where(y => definitions.Contains(y.Definition))));

            jobInfos.AddRange(existingJobInfos);
        }

        var newDefinitions = definitions.Except(jobInfos.Select(x => x.Definition)).ToArray();

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
                        new QueryRequestOptions { PartitionKey = new PartitionKey(JobGroupWrapper.JobInfoPartitionKey) }));

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
            GroupId = groupId ?? id,
            CreateDate = Clock.UtcNow,
            TimeToLive = (int)TimeSpan.FromDays(30).TotalSeconds,
        };

        var jobId = id;

        foreach (var item in definitions)
        {
            var definitionInfo = new JobDefinitionWrapper
            {
                JobId = jobId++,
                Status = isCompleted ? (byte)JobStatus.Completed : (byte)JobStatus.Created,
                Definition = item,
                DefinitionHash = item.ComputeHash(),
            };

            jobInfo.Definitions.Add(definitionInfo);
        }

        using IScoped<Container> container = _containerFactory.Invoke();
        ItemResponse<JobGroupWrapper> result = await container.Value.CreateItemAsync(jobInfo, new PartitionKey(JobGroupWrapper.JobInfoPartitionKey), cancellationToken: cancellationToken);

        return result.Resource.ToJobInfo().ToList();
    }

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

        return await _retryPolicy.ExecuteAsync(async () =>
            {
                FeedResponse<JobGroupWrapper> response = await ExecuteQueryAsync(sqlQuerySpec, 1, cancellationToken);

                JobGroupWrapper item = response.FirstOrDefault();
                if (item != null)
                {
                    return await DequeueItemsInternalAsync(item, numberOfJobsToDequeue, worker, heartbeatTimeoutSec, cancellationToken);
                }

                return Array.Empty<JobInfo>();
            });
    }

    private async Task<IReadOnlyCollection<JobInfo>> DequeueItemsInternalAsync(JobGroupWrapper item, int numberOfJobsToDequeue, string worker, int heartbeatTimeoutSec, CancellationToken cancellationToken, long? jobId = null)
    {
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
                job.Version = GenerateVersion();

                toReturn.Add(job);
            }
        }

        await SaveJobGroupAsync(item, cancellationToken);

        return item.ToJobInfo(toReturn).ToList();
    }

    public async Task<JobInfo> DequeueAsync(byte queueType, string worker, int heartbeatTimeoutSec, CancellationToken cancellationToken, long? jobId = null)
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
        }

        throw new JobNotExistException("Job not found.");
    }

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

    public async Task<IReadOnlyList<JobInfo>> GetJobByGroupIdAsync(byte queueType, long groupId, bool returnDefinition, CancellationToken cancellationToken)
    {
        JobGroupWrapper job = await GetGroupInternalAsync(queueType, groupId, cancellationToken);

        if (job != null)
        {
            return job.ToJobInfo(job.Definitions).ToList();
        }

        return null;
    }

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

    public async Task CancelJobByGroupIdAsync(byte queueType, long groupId, CancellationToken cancellationToken)
    {
        JobGroupWrapper job = await GetGroupInternalAsync(queueType, groupId, cancellationToken);
        if (job != null)
        {
            foreach (JobDefinitionWrapper item in job.Definitions)
            {
                if (item.Status == (byte)JobStatus.Running)
                {
                    item.CancelRequested = true;
                }
                else if (item.Status == (byte)JobStatus.Created)
                {
                    item.Status = (byte)JobStatus.Cancelled;
                }
            }

            await SaveJobGroupAsync(job, cancellationToken);
        }
    }

    public async Task CancelJobByIdAsync(byte queueType, long jobId, CancellationToken cancellationToken)
    {
        var jobs = await GetJobsByIdsInternalAsync(queueType, new[] { jobId }, false, cancellationToken);

        if (jobs.Count == 1)
        {
            (JobGroupWrapper JobGroup, IReadOnlyList<JobDefinitionWrapper> MatchingJob) job = jobs[0];
            JobDefinitionWrapper item = job.MatchingJob[0];

            if (item != null)
            {
                CancelJobDefinition(item);

                await SaveJobGroupAsync(job.JobGroup, cancellationToken);
            }
        }
    }

    public async Task CompleteJobAsync(JobInfo jobInfo, bool requestCancellationOnFailure, CancellationToken cancellationToken)
    {
        var jobs = await GetJobsByIdsInternalAsync(jobInfo.QueueType, new[] { jobInfo.Id }, false, cancellationToken);

        (JobGroupWrapper JobGroup, IReadOnlyList<JobDefinitionWrapper> MatchingJob) definitionTuple = jobs.Single();
        JobDefinitionWrapper item = definitionTuple.MatchingJob.Single();

        if (item != null)
        {
            if (jobInfo.Status == JobStatus.Failed)
            {
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
                item.Status = (byte?)jobInfo.Status ?? (byte?)JobStatus.Completed;
            }

            item.EndDate = Clock.UtcNow;
            item.Result = jobInfo.Result;

            await SaveJobGroupAsync(definitionTuple.JobGroup, cancellationToken);
        }
    }

    private async Task<JobGroupWrapper> GetGroupInternalAsync(
        byte queueType,
        long groupId,
        CancellationToken cancellationToken)
    {
            QueryDefinition sqlQuerySpec = new QueryDefinition(@"SELECT VALUE c FROM root c
           WHERE c.groupId = @groupId and c.queueType = @queueType")
                .WithParameter("@groupId", groupId)
                .WithParameter("@queueType", queueType);

            FeedResponse<JobGroupWrapper> response = await ExecuteQueryAsync(sqlQuerySpec, 1, cancellationToken);

            return response.FirstOrDefault();
    }

    private async Task<IReadOnlyList<(JobGroupWrapper JobGroup, IReadOnlyList<JobDefinitionWrapper> MatchingJob)>> GetJobsByIdsInternalAsync(byte queueType, long[] jobIds, bool returnDefinition, CancellationToken cancellationToken)
    {
        QueryDefinition sqlQuerySpec = new QueryDefinition(@$"SELECT VALUE c FROM root c JOIN d in c.definitions
           WHERE c.queueType = @queueType 
           AND ARRAY_CONTAINS([{string.Join(",", jobIds)}], d.jobId)")
            .WithParameter("@queueType", queueType);

        FeedResponse<JobGroupWrapper> response = await ExecuteQueryAsync(sqlQuerySpec, jobIds.Length, cancellationToken);

        var infos = new List<(JobGroupWrapper JobGroup, IReadOnlyList<JobDefinitionWrapper> MatchingJob)>();

        foreach (JobGroupWrapper item in response)
        {
            var scan = item.Definitions
                .Where(x => jobIds.Contains(x.JobId))
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

    private async Task<FeedResponse<JobGroupWrapper>> ExecuteQueryAsync(QueryDefinition sqlQuerySpec, int itemCount, CancellationToken cancellationToken)
    {
        using IScoped<Container> container = _containerFactory.Invoke();

        ICosmosQuery<JobGroupWrapper> query = _queryFactory.Create<JobGroupWrapper>(
            container.Value,
            new CosmosQueryContext(
                sqlQuerySpec,
                new QueryRequestOptions { PartitionKey = new PartitionKey(JobGroupWrapper.JobInfoPartitionKey), MaxItemCount = itemCount }));

        FeedResponse<JobGroupWrapper> response = await query.ExecuteNextAsync(cancellationToken);
        return response;
    }

    private async Task SaveJobGroupAsync(JobGroupWrapper definition, CancellationToken cancellationToken)
    {
        using IScoped<Container> container = _containerFactory.Invoke();

        try
        {
            await container.Value.UpsertItemAsync(
                definition,
                new PartitionKey(JobGroupWrapper.JobInfoPartitionKey),
                new ItemRequestOptions { IfMatchEtag = definition.ETag },
                cancellationToken: cancellationToken);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw new RetriableJobException("Job precondition failed.", ex);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new RetriableJobException("Service too busy.", ex);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.RequestEntityTooLarge)
        {
            throw new JobExecutionException("Job data too large.", ex);
        }
    }

    private static long GetLongId()
    {
        return IdHelper.DateToId(Clock.UtcNow.DateTime) + GenerateRandomNumber();
    }

    /// <summary>
    /// To generate a random number between 100 and 999,
    /// the method uses the formula (randomValue / UInt16.MaxValue) * 899 + 100.
    /// This takes the percentage of the maximum UInt16 value that the generated value represents,
    /// multiplies it by 899 (the difference between 999 and 100),
    /// and adds 100 to shift the range to between 100 and 999.
    /// </summary>
    private static int GenerateRandomNumber()
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[2];
        rng.GetBytes(bytes);

        var value = BitConverter.ToUInt16(bytes, 0);
        var percentage = (double)value / ushort.MaxValue;

        var randomNumber = (int)Math.Round(percentage * 899) + 100;
        return randomNumber;
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
