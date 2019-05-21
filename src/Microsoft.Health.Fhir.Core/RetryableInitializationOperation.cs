// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core
{
    /// <summary>
    /// Encapsulates a thread-safe, retryable asynchronous initialization operation that is lazily invoked.
    /// The encapsulated operation will awaited until it completes. If it fails, the next time
    /// <see cref="EnsureInitialized"/> in called, the operation is restarted.
    /// </summary>
    public class RetryableInitializationOperation : IDisposable
    {
        private readonly Func<Task> _operation;
        private SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private Task _task;

        public RetryableInitializationOperation(Func<Task> operation)
        {
            EnsureArg.IsNotNull(operation, nameof(operation));
            _operation = operation;
        }

        /// <summary>
        /// Peeks to see if the value has been initialized
        /// </summary>
        public bool IsInitialized => _task?.IsCompletedSuccessfully == true;

        /// <summary>
        /// When invoked for the first time, starts the async operation
        /// and awaits its completion. If the task succeeds, subsequent invocations
        /// of this method will always return a completed task. If the task fails,
        /// the next call will restart the operation. The restart is done with
        /// synchronization so only one running task will exist at a time.
        /// </summary>
        /// <returns>A task representing the completion of the initialization operation.</returns>
        public async ValueTask EnsureInitialized()
        {
            if (IsInitialized)
            {
                return;
            }

            if (_task == null)
            {
                await _semaphore.WaitAsync();

                try
                {
                    if (_task == null)
                    {
                        _task = _operation();
                    }
                }
                finally
                {
                    _semaphore.Release();
                }
            }

            if (_task.IsFaulted)
            {
                await _semaphore.WaitAsync();

                try
                {
                    if (_task.IsFaulted)
                    {
                        // try again
                        _task = _operation();
                    }
                }
                finally
                {
                    _semaphore.Release();
                }
            }

            await _task;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _semaphore?.Dispose();
                _semaphore = null;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
