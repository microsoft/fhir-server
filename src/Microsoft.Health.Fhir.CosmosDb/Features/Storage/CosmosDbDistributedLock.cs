// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
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

        private readonly Uri _collectionUri;
        private readonly string _lockId;
        private readonly ILogger<CosmosDbDistributedLock> _logger;
        private readonly TimeSpan _lockDocumentTimeToLive;
        private readonly LockDocument _lockDocument;
        private CancellationTokenSource _keepAliveCancellationSource;
        private Task _keepAliveTask;
        private string _lockDocumentSelfLink;
        private readonly Func<IScoped<IDocumentClient>> _documentClientFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosDbDistributedLock"/> class.
        /// Note that the lock will not be acquired until <see cref="AcquireLock"/> is called.
        /// </summary>
        /// <param name="documentClientFactory">The document client factory</param>
        /// <param name="collectionUri">The URI of the collection to use</param>
        /// <param name="lockId">The id of the lock. The document created in the database will use this id and will be prefixed with <see cref="IdPrefix"/>.</param>
        /// <param name="logger">A logger instance</param>
        public CosmosDbDistributedLock(Func<IScoped<IDocumentClient>> documentClientFactory, Uri collectionUri, string lockId, ILogger<CosmosDbDistributedLock> logger)
            : this(documentClientFactory, collectionUri, lockId, logger, DefaultLockDocumentTimeToLive)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosDbDistributedLock"/> class.
        /// Note that the lock will not be acquired until <see cref="AcquireLock"/> is called.
        /// </summary>
        /// <param name="documentClientFactory">The document client factory</param>
        /// <param name="collectionUri">The URI of the collection to use</param>
        /// <param name="lockId">The id of the lock. The document created in the database will use this ID will be prefixed with <see cref="IdPrefix"/>.</param>
        /// <param name="logger">A logger instance</param>
        /// <param name="lockDocumentTimeToLive">The time to live for the lock document. If the process crashes, the lock will be released after this amount of time</param>
        public CosmosDbDistributedLock(Func<IScoped<IDocumentClient>> documentClientFactory, Uri collectionUri, string lockId, ILogger<CosmosDbDistributedLock> logger, TimeSpan lockDocumentTimeToLive)
        {
            EnsureArg.IsNotNull(collectionUri, nameof(collectionUri));
            EnsureArg.IsNotNull(documentClientFactory, nameof(documentClientFactory));
            EnsureArg.IsNotNullOrWhiteSpace(lockId, nameof(lockId));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _collectionUri = collectionUri;
            _lockId = lockId;
            _logger = logger;
            _lockDocumentTimeToLive = lockDocumentTimeToLive;
            _lockDocument = new LockDocument { Id = IdPrefix + lockId, TimeToLiveInSeconds = (int)lockDocumentTimeToLive.TotalSeconds };
            _documentClientFactory = documentClientFactory;
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
                using (var scopedDocumentClient = _documentClientFactory.Invoke())
                {
                    var response = await scopedDocumentClient.Value.CreateDocumentAsync(_collectionUri, _lockDocument);

                    _lockDocumentSelfLink = response.Resource.SelfLink;
                    _keepAliveCancellationSource = new CancellationTokenSource();
                    _keepAliveTask = LockKeepAliveLoop(_keepAliveCancellationSource.Token);

                    _logger.LogInformation("Lock {LockId} acquired", _lockId);
                    return true;
                }
            }
            catch (DocumentClientException e) when (e.StatusCode == HttpStatusCode.Conflict)
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
                _keepAliveCancellationSource.Cancel();
                try
                {
                    await _keepAliveTask;
                }
                catch (OperationCanceledException)
                {
                }
                catch (DocumentClientException)
                {
                }

                try
                {
                    using (var scopedDocumentClient = _documentClientFactory.Invoke())
                    {
                        await scopedDocumentClient.Value.DeleteDocumentAsync(_lockDocumentSelfLink, new RequestOptions { PartitionKey = new PartitionKey(LockDocument.LockPartition) });
                    }
                }
                catch (DocumentClientException e) when (e.StatusCode == HttpStatusCode.NotFound)
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
        /// Synchronously releases the lock if <see cref="ReleaseLock"/> has not already been called.
        /// </summary>
        public void Dispose()
        {
            if (IsLockHeld)
            {
                ReleaseLock().GetAwaiter().GetResult();
            }
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
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_lockDocumentTimeToLive.TotalSeconds / 3), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        using (var scopedDocumentClient = _documentClientFactory.Invoke())
                        {
                            await scopedDocumentClient.Value.UpsertDocumentAsync(_collectionUri, _lockDocument);
                        }

                        break;
                    }
                    catch (RequestRateExceededException)
                    {
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

            [JsonProperty(KnownResourceWrapperProperties.PartitionKey)]
            public string PartitionKey { get; } = LockPartition;
        }
    }
}
