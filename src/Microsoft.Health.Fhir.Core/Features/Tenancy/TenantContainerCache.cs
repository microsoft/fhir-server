// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Options;

namespace Microsoft.Health.Fhir.Core.Features.Tenancy
{
    /// <summary>
    /// Default <see cref="ITenantContainerCache"/> implementation with bounded admission and idle eviction.
    /// </summary>
    /// <remarks>
    /// The single admission semaphore deliberately serializes all tenant-container construction. This is
    /// load-bearing because container construction projects process-shared Firely
    /// <c>ITypedElement</c> trees. Parallelizing admission first requires proving Firely's element model safe
    /// for concurrent reads; the gate is not merely a same-tenant construction optimization.
    /// </remarks>
    public sealed class TenantContainerCache : ITenantContainerCache
    {
        private const int StateOpen = 0;
        private const int StateDisposing = 1;
        private const int StateDisposed = 2;

        private readonly ConcurrentDictionary<TenantId, ITenantContainer> _containers = new();
        private readonly SemaphoreSlim _admissionGate = new(1, 1);
        private readonly object _lifecycleSync = new();
        private readonly TaskCompletionSource _operationsDrained =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource _disposalCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly ITenantContainerFactory _factory;
        private readonly TenantContainerCacheOptions _options;
        private readonly TimeProvider _timeProvider;

        private int _activeOperations;
        private int _admissionRejectionCount;
        private int _evictionCount;
        private int _state = StateOpen;

        /// <summary>
        /// Initializes a new instance of the <see cref="TenantContainerCache"/> class.
        /// </summary>
        /// <param name="factory">The factory used to build containers.</param>
        /// <param name="options">The cache options.</param>
        /// <param name="timeProvider">The time provider used for idle tracking.</param>
        public TenantContainerCache(
            ITenantContainerFactory factory,
            IOptions<TenantContainerCacheOptions> options,
            TimeProvider timeProvider)
        {
            EnsureArg.IsNotNull(factory, nameof(factory));
            EnsureArg.IsNotNull(options?.Value, nameof(options));
            EnsureArg.IsNotNull(timeProvider, nameof(timeProvider));

            _factory = factory;
            _options = options.Value;
            _timeProvider = timeProvider;
        }

        /// <inheritdoc />
        public int Count => _containers.Count;

        /// <summary>
        /// Gets the configured resident cap.
        /// </summary>
        public int MaxResidentTenants => _options.MaxResidentTenants;

        /// <summary>
        /// Gets the number of containers evicted over the lifetime of this cache.
        /// </summary>
        public int EvictionCount => Volatile.Read(ref _evictionCount);

        /// <summary>
        /// Gets the number of admission rejections over the lifetime of this cache.
        /// </summary>
        public int AdmissionRejectionCount => Volatile.Read(ref _admissionRejectionCount);

        /// <inheritdoc />
        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "The fresh lease is transferred to the caller after publication or disposed in the ownership-cleanup finally block.")]
        public async ValueTask<ITenantLease> AcquireAsync(
            TenantDescriptor tenant,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(tenant, nameof(tenant));
            EnterOperation();

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_containers.TryGetValue(tenant.TenantId, out ITenantContainer cached) &&
                    cached.TryAcquire(out ITenantLease cachedLease))
                {
                    return ReturnLeaseIfOpenOrDispose(cachedLease, cancellationToken);
                }

                ThrowIfNotOpen();
                await _admissionGate.WaitAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    ThrowIfNotOpen();

                    if (_containers.TryGetValue(tenant.TenantId, out ITenantContainer existing))
                    {
                        if (existing.TryAcquire(out ITenantLease existingLease))
                        {
                            return ReturnLeaseIfOpenOrDispose(existingLease, cancellationToken);
                        }

                        await RemoveAndDisposeAsync(existing, cancellationToken).ConfigureAwait(false);
                        ThrowIfNotOpen();
                    }

                    await MakeRoomAsync(tenant.TenantId, cancellationToken).ConfigureAwait(false);
                    ThrowIfNotOpen();

                    ITenantContainer created = await _factory
                        .CreateAsync(tenant, cancellationToken)
                        .ConfigureAwait(false);

                    ITenantLease createdLease = null;
                    List<ExceptionDispatchInfo> failures = [];

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        createdLease = AcquireFreshLease(created, tenant.TenantId);

                        Publish(tenant.TenantId, created, cancellationToken);

                        ITenantLease leaseToReturn = createdLease;
                        createdLease = null;
                        return leaseToReturn;
                    }
                    catch (Exception exception)
                    {
                        AddFailure(failures, exception);
                    }
                    finally
                    {
                        try
                        {
                            createdLease?.Dispose();
                        }
                        catch (Exception cleanupException)
                        {
                            AddFailure(failures, cleanupException);
                        }

                        createdLease = null;
                    }

                    await DisposeContainersAsync([created], failures).ConfigureAwait(false);
                    return ThrowFailures<ITenantLease>(failures);
                }
                finally
                {
                    _admissionGate.Release();
                }
            }
            finally
            {
                ExitOperation();
            }
        }

        /// <inheritdoc />
        public async ValueTask EvictIdleAsync(CancellationToken cancellationToken)
        {
            EnterOperation();

            try
            {
                ThrowIfNotOpen();
                await _admissionGate.WaitAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    ThrowIfNotOpen();
                    await EvictIdleCoreAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _admissionGate.Release();
                }
            }
            finally
            {
                ExitOperation();
            }
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            bool startDisposal = false;

            lock (_lifecycleSync)
            {
                if (_state == StateOpen)
                {
                    Volatile.Write(ref _state, StateDisposing);
                    startDisposal = true;

                    if (_activeOperations == 0)
                    {
                        _operationsDrained.TrySetResult();
                    }
                }
            }

            if (startDisposal)
            {
                // The completion source is published before cleanup starts, so every caller observes the
                // same completion and failure without running user cleanup while the lifecycle lock is held.
                _ = CompleteDisposalAsync();
            }

            return new ValueTask(_disposalCompletion.Task);
        }

        private async Task CompleteDisposalAsync()
        {
            try
            {
                await DisposeCoreAsync().ConfigureAwait(false);
                _disposalCompletion.TrySetResult();
            }
            catch (Exception exception)
            {
                _disposalCompletion.TrySetException(exception);
            }
        }

        private async Task DisposeCoreAsync()
        {
            await _operationsDrained.Task.ConfigureAwait(false);

            List<ITenantContainer> ownedContainers = _containers.Values
                .OrderBy(container => container.TenantId.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (ITenantContainer container in ownedContainers)
            {
                _containers.TryRemove(container.TenantId, out _);
            }

            List<ExceptionDispatchInfo> failures = [];
            await DisposeContainersAsync(ownedContainers, failures).ConfigureAwait(false);

            try
            {
                _admissionGate.Dispose();
            }
            catch (Exception exception)
            {
                AddFailure(failures, exception);
            }

            Volatile.Write(ref _state, StateDisposed);
            ThrowIfAnyFailures(failures);
        }

        private async ValueTask MakeRoomAsync(TenantId incoming, CancellationToken cancellationToken)
        {
            if (_containers.Count < _options.MaxResidentTenants)
            {
                return;
            }

            await EvictIdleCoreAsync(cancellationToken).ConfigureAwait(false);

            while (_containers.Count >= _options.MaxResidentTenants)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfNotOpen();

                ITenantContainer victim = _containers.Values
                    .Where(container => container.ActiveLeaseCount == 0)
                    .OrderBy(container => container.LastAccessedUtc)
                    .ThenBy(container => container.TenantId.Value, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (victim is null)
                {
                    Interlocked.Increment(ref _admissionRejectionCount);
                    throw new TenantAdmissionRejectedException(incoming, _options.MaxResidentTenants);
                }

                await RemoveAndDisposeAsync(victim, cancellationToken).ConfigureAwait(false);
            }
        }

        private async ValueTask EvictIdleCoreAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DateTimeOffset cutoff = _timeProvider.GetUtcNow() - _options.IdleTimeout;
            List<ITenantContainer> candidates = _containers.Values
                .Where(container => container.ActiveLeaseCount == 0 && container.LastAccessedUtc <= cutoff)
                .OrderBy(container => container.TenantId.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();
            List<ITenantContainer> removed = [];
            List<ExceptionDispatchInfo> failures = [];
            bool cancellationCaptured = false;

            foreach (ITenantContainer candidate in candidates)
            {
                if (CaptureCancellation(failures, ref cancellationCaptured, cancellationToken))
                {
                    break;
                }

                if (candidate.ActiveLeaseCount == 0 &&
                    candidate.LastAccessedUtc <= cutoff &&
                    _containers.TryRemove(candidate.TenantId, out _))
                {
                    removed.Add(candidate);
                    Interlocked.Increment(ref _evictionCount);
                }
            }

            CaptureCancellation(failures, ref cancellationCaptured, cancellationToken);
            await DisposeContainersAsync(removed, failures).ConfigureAwait(false);
            CaptureCancellation(failures, ref cancellationCaptured, cancellationToken);
            ThrowIfAnyFailures(failures);
        }

        private async ValueTask RemoveAndDisposeAsync(
            ITenantContainer container,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_containers.TryRemove(container.TenantId, out _))
            {
                return;
            }

            Interlocked.Increment(ref _evictionCount);
            List<ExceptionDispatchInfo> failures = [];
            bool cancellationCaptured = false;
            CaptureCancellation(failures, ref cancellationCaptured, cancellationToken);
            await DisposeContainersAsync([container], failures).ConfigureAwait(false);
            CaptureCancellation(failures, ref cancellationCaptured, cancellationToken);
            ThrowIfAnyFailures(failures);
        }

        private void Publish(
            TenantId tenantId,
            ITenantContainer container,
            CancellationToken cancellationToken)
        {
            lock (_lifecycleSync)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_state != StateOpen)
                {
                    ThrowDisposed();
                }

                if (!_containers.TryAdd(tenantId, container))
                {
                    throw new InvalidOperationException(
                        $"A container for tenant '{tenantId}' was published while admission was serialized.");
                }
            }
        }

        private void EnterOperation()
        {
            lock (_lifecycleSync)
            {
                if (_state != StateOpen)
                {
                    ThrowDisposed();
                }

                _activeOperations++;
            }
        }

        private static ITenantLease AcquireFreshLease(ITenantContainer container, TenantId tenantId)
        {
            if (container.TryAcquire(out ITenantLease lease))
            {
                return lease;
            }

            throw new InvalidOperationException(
                $"A freshly created container for tenant '{tenantId}' refused a lease.");
        }

        private ITenantLease ReturnLeaseIfOpenOrDispose(
            ITenantLease lease,
            CancellationToken cancellationToken)
        {
            if (Volatile.Read(ref _state) == StateOpen &&
                !cancellationToken.IsCancellationRequested)
            {
                return lease;
            }

            lease.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
            throw new ObjectDisposedException(nameof(TenantContainerCache));
        }

        private void ExitOperation()
        {
            lock (_lifecycleSync)
            {
                _activeOperations--;

                if (_state != StateOpen && _activeOperations == 0)
                {
                    _operationsDrained.TrySetResult();
                }
            }
        }

        private void ThrowIfNotOpen()
        {
            if (Volatile.Read(ref _state) != StateOpen)
            {
                ThrowDisposed();
            }
        }

        [DoesNotReturn]
        private static void ThrowDisposed() => throw new ObjectDisposedException(nameof(TenantContainerCache));

        private static bool CaptureCancellation(
            List<ExceptionDispatchInfo> failures,
            ref bool cancellationCaptured,
            CancellationToken cancellationToken)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (!cancellationCaptured)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException exception)
                {
                    AddFailure(failures, exception);
                    cancellationCaptured = true;
                }
            }

            return true;
        }

        private static async Task DisposeContainersAsync(
            List<ITenantContainer> containers,
            List<ExceptionDispatchInfo> failures)
        {
            var disposalTasks = new List<Task>(containers.Count);

            foreach (ITenantContainer container in containers)
            {
                try
                {
                    disposalTasks.Add(container.DisposeAsync().AsTask());
                }
                catch (Exception exception)
                {
                    disposalTasks.Add(Task.FromException(exception));
                }
            }

            foreach (Task disposalTask in disposalTasks)
            {
                try
                {
                    await disposalTask.ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    AddFailure(failures, exception);
                }
            }
        }

        private static void AddFailure(List<ExceptionDispatchInfo> failures, Exception exception)
        {
            if (exception is AggregateException aggregateException &&
                aggregateException.InnerExceptions.Count > 0)
            {
                foreach (Exception innerException in aggregateException.InnerExceptions)
                {
                    AddFailure(failures, innerException);
                }

                return;
            }

            failures.Add(ExceptionDispatchInfo.Capture(exception));
        }

        private static void ThrowIfAnyFailures(List<ExceptionDispatchInfo> failures)
        {
            if (failures.Count > 0)
            {
                _ = ThrowFailures<object>(failures);
            }
        }

        [DoesNotReturn]
        private static T ThrowFailures<T>(List<ExceptionDispatchInfo> failures)
        {
            if (failures.Count == 1)
            {
                failures[0].Throw();
            }

            throw new AggregateException(failures.Select(failure => failure.SourceException));
        }
    }
}
