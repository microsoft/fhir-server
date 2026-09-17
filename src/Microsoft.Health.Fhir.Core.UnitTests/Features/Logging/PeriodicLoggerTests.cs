// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Logging;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Logging
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    public class PeriodicLoggerTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void GivenNonPositiveInterval_WhenConstructing_ThenThrows(double seconds)
        {
            var logger = new RecordingLogger();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => new PeriodicLogger(logger, TimeSpan.FromSeconds(seconds)));
        }

        [Fact]
        public void GivenNullLogger_WhenConstructing_ThenThrows()
        {
            Assert.Throws<ArgumentNullException>(
                () => new PeriodicLogger(null, TimeSpan.FromMinutes(1)));
        }

        [Fact]
        public async Task GivenWrappedLogger_WhenCheckingContract_ThenDelegates()
        {
            var innerLogger = new RecordingLogger { Enabled = false };
            await using var logger = new PeriodicLogger(innerLogger, TimeSpan.FromMinutes(1));
            object scopeState = new();

            IDisposable scope = logger.BeginScope(scopeState);
            bool enabled = logger.IsEnabled(LogLevel.Warning);

            Assert.Same(innerLogger.Scope, scope);
            Assert.Same(scopeState, innerLogger.ScopeState);
            Assert.False(enabled);
            Assert.Equal(LogLevel.Warning, innerLogger.LastEnabledLevel);
        }

        [Theory]
        [InlineData(LogLevel.Trace)]
        [InlineData(LogLevel.Debug)]
        [InlineData(LogLevel.Warning)]
        [InlineData(LogLevel.Error)]
        [InlineData(LogLevel.Critical)]
        [InlineData(LogLevel.None)]
        public async Task GivenNonInformationMessage_WhenLogging_ThenWritesImmediately(LogLevel level)
        {
            var innerLogger = new RecordingLogger();
            await using var logger = new PeriodicLogger(innerLogger, TimeSpan.FromMinutes(1));
            var eventId = new EventId(17, "Immediate");
            var exception = new InvalidOperationException("failure");

            logger.Log(level, eventId, "message", exception, static (state, _) => state);

            RecordedLog record = Assert.Single(innerLogger.Records);
            Assert.Equal(level, record.Level);
            Assert.Equal(eventId, record.EventId);
            Assert.Same(exception, record.Exception);
            Assert.Equal("message", record.Message);
        }

        private sealed class RecordingLogger : ILogger
        {
            public ConcurrentQueue<RecordedLog> Records { get; } = new();

            public IDisposable Scope { get; } = new TestScope();

            public object ScopeState { get; private set; }

            public LogLevel LastEnabledLevel { get; private set; }

            public bool Enabled { get; set; } = true;

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull
            {
                ScopeState = state;
                return Scope;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                LastEnabledLevel = logLevel;
                return Enabled;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter)
            {
                ArgumentNullException.ThrowIfNull(formatter);

                IReadOnlyList<KeyValuePair<string, object>> structuredState = state is IEnumerable<KeyValuePair<string, object>> structuredStateValues
                    ? structuredStateValues.Select(item => new KeyValuePair<string, object>(item.Key, item.Value)).ToArray()
                    : null;

                Records.Enqueue(new RecordedLog(logLevel, eventId, exception, formatter(state, exception), structuredState));
            }
        }

        private sealed record RecordedLog(
            LogLevel Level,
            EventId EventId,
            Exception Exception,
            string Message,
            IReadOnlyList<KeyValuePair<string, object>> StructuredState);

        private sealed class TestScope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
