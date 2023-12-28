// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>
    /// A simple distributed lock based on a Cosmos DB document with a defined TTL. The presence of the document
    /// implies that the lock is held. The lock is released by deleting the document, or, if the process crashes,
    /// it will be automatically deleted when the TTL lapses. Once the lock is held, this instance takes care of
    /// periodically updating the document to push out the TTL.
    /// </summary>
    public sealed class CosmosDbDistributedLock : ICosmosDbDistributedLock
    {
        private const string IdPrefix = "LockDocument:";
        private static readonly TimeSpan InitialRetryWaitPeriod = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan MaxRetryWaitPeriod = TimeSpan.FromSeconds(8);

        private static readonly TimeSpan DefaultLockDocumentTimeToLive = TimeSpan.FromSeconds(30);

        private readonly string _lockId;
        private readonly ILogger<CosmosDbDistributedLock> _logger;
        private readonly TimeSpan _lockDocumentTimeToLive;
        private readonly LockDocument _lockDocument;
        private CancellationTokenSource _keepAliveCancellationSource;
        private Task _keepAliveTask;
        private readonly Func<IScoped<Container>> _containerFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosDbDistributedLock"/> class.
        /// Note that the lock will not be acquired until <see cref="AcquireLock"/> is called.
        /// </summary>
        /// <param name="containerFactory">The Cosmos Container factory</param>
        /// <param name="lockId">The id of the lock. The document created in the database will use this id and will be prefixed with <see cref="IdPrefix"/>.</param>
        /// <param name="logger">A logger instance</param>
        public CosmosDbDistributedLock(Func<IScoped<Container>> containerFactory, string lockId, ILogger<CosmosDbDistributedLock> logger)
            : this(containerFactory, lockId, logger, DefaultLockDocumentTimeToLive)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosDbDistributedLock"/> class.
        /// Note that the lock will not be acquired until <see cref="AcquireLock"/> is called.
        /// </summary>
        /// <param name="containerFactory">The Cosmos Container factory</param>
        /// <param name="lockId">The id of the lock. The document created in the database will use this ID will be prefixed with <see cref="IdPrefix"/>.</param>
        /// <param name="logger">A logger instance</param>
        /// <param name="lockDocumentTimeToLive">The time to live for the lock document. If the process crashes, the lock will be released after this amount of time</param>
        public CosmosDbDistributedLock(Func<IScoped<Container>> containerFactory, string lockId, ILogger<CosmosDbDistributedLock> logger, TimeSpan lockDocumentTimeToLive)
        {
            EnsureArg.IsNotNull(containerFactory, nameof(containerFactory));
            EnsureArg.IsNotNullOrWhiteSpace(lockId, nameof(lockId));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _lockId = lockId;
            _logger = logger;
            _lockDocumentTimeToLive = lockDocumentTimeToLive;
            _lockDocument = new LockDocument { Id = IdPrefix + lockId, TimeToLiveInSeconds = (int)lockDocumentTimeToLive.TotalSeconds };
            _containerFactory = containerFactory;
        }

        private bool IsLockHeld => _keepAliveTask != null;

        /// <summary>
        /// Acquires the lock.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token</param>
        /// <returns>A task that completes when the lock has been acquired.</returns>
        public async Task AcquireLock(CancellationToken cancellationToken)
        {
            TimeSpan delay = InitialRetryWaitPeriod;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await TryAcquireLock())
                {
                    return;
                }

                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromSeconds(Math.Min(MaxRetryWaitPeriod.TotalSeconds, delay.TotalSeconds * 2));
            }
        }

        /// <summary>
        /// Makes one attempt to acquire the lock.
        /// </summary>
        /// <returns>A task that completes when the attempt completes. The boolean task argument indicates whether the lock was successfully acquired</returns>
        public async Task<bool> TryAcquireLock()
        {
            if (IsLockHeld)
            {
                throw new InvalidOperationException("Lock is already held.");
            }

            try
            {
                using (IScoped<Container> containerScope = _containerFactory.Invoke())
                {
                    await containerScope.Value.CreateItemAsync(
                        _lockDocument,
                        new PartitionKey(LockDocument.LockPartition));

                    _keepAliveCancellationSource = new CancellationTokenSource();
                    _keepAliveTask = LockKeepAliveLoop(_keepAliveCancellationSource.Token);

                    _logger.LogInformation("Lock {LockId} acquired", _lockId);
                    return true;
                }
            }
            catch (CosmosException e) when (e.StatusCode == HttpStatusCode.Conflict)
            {
                return false;
            }
        }

        /// <summary>
        /// Releases the lock.
        /// </summary>
        /// <returns>A task that completes when the lock has been released</returns>
        public async Task ReleaseLock()
        {
            if (!IsLockHeld)
            {
                throw new InvalidOperationException("Cannot release lock that was not held");
            }

            try
            {
                await _keepAliveCancellationSource.CancelAsync();

                try
                {
                    await _keepAliveTask;
                }
                catch (OperationCanceledException)
                {
                    // Ignore because lock is being released
                }
                catch (CosmosException)
                {
                    // Ignore because lock is being released
                }

                try
                {
                    using (IScoped<Container> containerScope = _containerFactory.Invoke())
                    {
                        await containerScope.Value.DeleteItemAsync<LockDocument>(
                            _lockDocument.Id,
                            new PartitionKey(LockDocument.LockPartition));
                    }
                }
                catch (CosmosException e) when (e.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogWarning(e, "Lock {LockId} was not held when released", _lockId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error when releasing lock {LockId}", _lockId);
                    throw;
                }

                _logger.LogInformation("Lock {LockId} released", _lockId);
            }
            finally
            {
                _keepAliveCancellationSource.Dispose();
                _keepAliveCancellationSource = null;
                _keepAliveTask.Dispose();
                _keepAliveTask = null;
            }
        }

        /// <summary>
        /// Releases the lock if <see cref="ReleaseLock"/> has not already been called.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (IsLockHeld)
            {
                await ReleaseLock();
            }
        }

        /// <summary>
        /// Synchronously releases the lock if <see cref="ReleaseLock"/> has not already been called.
        /// </summary>
        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// A background task that keeps the lock active.
        /// </summary>
        /// <param name="cancellationToken">The token to cancel the loop.</param>
        /// <returns>A task representing the operation</returns>
        private async Task LockKeepAliveLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Create a task that completes when the cancellationToken is canceled.
                // We do this to avoid and exception that occurs on every startup
                // and can cause the debugger to break.

                var cancellationCompletionSource = new TaskCompletionSource<object>();
                cancellationToken.Register(() => cancellationCompletionSource.SetResult(null));

                await Task.WhenAny(
                    Task.Delay(TimeSpan.FromSeconds(_lockDocumentTimeToLive.TotalSeconds / 3), CancellationToken.None),
                    cancellationCompletionSource.Task);

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        using (IScoped<Container> containerScope = _containerFactory.Invoke())
                        {
                            await containerScope.Value.UpsertItemAsync(_lockDocument, new PartitionKey(_lockDocument.PartitionKey), cancellationToken: cancellationToken);
                        }

                        break;
                    }
                    catch (RequestRateExceededException)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None);
                    }
                }
            }
        }

        private class LockDocument : SystemData
        {
            internal const string LockPartition = "_locks";

            // used to set expiration policy
            [JsonProperty(PropertyName = "ttl")]
            public int TimeToLiveInSeconds { get; set; }

            [JsonProperty(KnownDocumentProperties.PartitionKey)]
            public string PartitionKey { get; } = LockPartition;
        }
    }
}
