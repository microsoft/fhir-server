// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core.Features.Logging
{
    /// <summary>
    /// Wraps an <see cref="ILogger"/> and introduces a periodic flush loop for later batching behavior.
    /// </summary>
    public sealed class PeriodicLogger : ILogger, IDisposable, IAsyncDisposable
    {
        private readonly ILogger _logger;
        private readonly TimeSpan _interval;
        private readonly TimeProvider _timeProvider;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly Task _flushLoopTask;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="PeriodicLogger"/> class.
        /// </summary>
        /// <param name="logger">The wrapped logger.</param>
        /// <param name="interval">The flush interval.</param>
        /// <param name="timeProvider">The time provider used to drive the periodic timer.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logger"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="interval"/> is not positive.</exception>
        public PeriodicLogger(ILogger logger, TimeSpan interval, TimeProvider timeProvider = null)
        {
            ArgumentNullException.ThrowIfNull(logger);

            if (interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(interval), interval, "The logging interval must be greater than zero.");
            }

            _logger = logger;
            _interval = interval;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _flushLoopTask = RunFlushLoopAsync(_cancellationTokenSource.Token);
        }

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _logger.BeginScope(state);
        }

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _logger.IsEnabled(logLevel);
        }

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(formatter);

            if (!_logger.IsEnabled(logLevel))
            {
                return;
            }

            if (logLevel != LogLevel.Information)
            {
                _logger.Log(logLevel, eventId, state, exception, formatter);
            }
        }

        /// <summary>
        /// Disposes the logger synchronously.
        /// </summary>
        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        /// <summary>
        /// Disposes the logger asynchronously.
        /// </summary>
        /// <returns>A task that completes when disposal is finished.</returns>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            await _cancellationTokenSource.CancelAsync().ConfigureAwait(false);
            await _flushLoopTask.ConfigureAwait(false);
            _cancellationTokenSource.Dispose();
        }

        /// <summary>
        /// Runs the periodic flush loop until cancellation is requested.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token used to stop the loop.</param>
        /// <returns>A task representing the background loop.</returns>
        private async Task RunFlushLoopAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(_interval, _timeProvider);

            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }
}
