// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// A retry-enabled test case for theories with non-serializable data.
    /// This delays data enumeration until runtime (like XunitTheoryTestCase)
    /// and wraps the entire theory execution with retry logic.
    /// </summary>
    public class RetryDelayEnumeratedTheoryTestCase : XunitTheoryTestCase
    {
        private int _maxRetries;
        private int _delayMs;
        private bool _retryOnAssertionFailure;

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
        public RetryDelayEnumeratedTheoryTestCase()
        {
        }

        public RetryDelayEnumeratedTheoryTestCase(
            IMessageSink diagnosticMessageSink,
            TestMethodDisplay defaultMethodDisplay,
            TestMethodDisplayOptions defaultMethodDisplayOptions,
            ITestMethod testMethod,
            int maxRetries,
            int delayMs,
            bool retryOnAssertionFailure = false)
            : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod)
        {
            _maxRetries = maxRetries;
            _delayMs = delayMs;
            _retryOnAssertionFailure = retryOnAssertionFailure;
        }

        public override async Task<RunSummary> RunAsync(
            IMessageSink diagnosticMessageSink,
            IMessageBus messageBus,
            object[] constructorArguments,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource)
        {
            Trace.WriteLine($"##vso[task.logdetail]RetryTheory starting test '{TestMethod.TestClass.Class.Name}.{TestMethod.Method.Name}' with MaxRetries={_maxRetries}, DelayMs={_delayMs}, RetryOnAssertionFailure={_retryOnAssertionFailure}");

            var runSummary = new RunSummary();
            Exception lastException = null;

            for (int attempt = 1; attempt <= _maxRetries; attempt++)
            {
                var isLastAttempt = attempt == _maxRetries;

                Trace.WriteLine($"##vso[task.logdetail]RetryTheory attempt {attempt}/{_maxRetries} for test '{TestMethod.Method.Name}'");

                // Create a fresh aggregator for each attempt
                var attemptAggregator = new ExceptionAggregator();

                // Only intercept failure messages on non-final attempts
                if (isLastAttempt)
                {
                    try
                    {
                        var summary = await base.RunAsync(
                            diagnosticMessageSink,
                            messageBus,
                            constructorArguments,
                            attemptAggregator,
                            cancellationTokenSource);

                        runSummary = summary;

                        if (summary.Failed == 0)
                        {
                            Trace.WriteLine($"##vso[task.logdetail]RetryTheory test '{TestMethod.TestClass.Class.Name}.{TestMethod.Method.Name}' passed on attempt {attempt}/{_maxRetries}");
                            diagnosticMessageSink.OnMessage(
                                new DiagnosticMessage($"[RetryTheory] Test '{TestMethod.TestClass.Class.Name}.{TestMethod.Method.Name}' passed on attempt {attempt}/{_maxRetries}"));
                            return summary;
                        }

                        lastException = attemptAggregator.ToException();
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        throw;
                    }
                }
                else
                {
                    using (var interceptingBus = new FailureInterceptingMessageBus(messageBus))
                    {
                        try
                        {
                            var summary = await base.RunAsync(
                                diagnosticMessageSink,
                                interceptingBus,
                                constructorArguments,
                                attemptAggregator,
                                cancellationTokenSource);

                            runSummary = summary;

                            if (summary.Failed == 0)
                            {
                                Trace.WriteLine($"##vso[task.logdetail]RetryTheory test '{TestMethod.TestClass.Class.Name}.{TestMethod.Method.Name}' passed on attempt {attempt}/{_maxRetries}");
                                diagnosticMessageSink.OnMessage(
                                    new DiagnosticMessage($"[RetryTheory] Test '{TestMethod.TestClass.Class.Name}.{TestMethod.Method.Name}' passed on attempt {attempt}/{_maxRetries}"));
                                return summary;
                            }

                            // Test failed on this attempt
                            lastException = attemptAggregator.ToException();

                            // If no exception was captured but test failed, create an exception using captured failure details
                            if (lastException == null && summary.Failed > 0)
                            {
                                string failureMsg = interceptingBus.LastFailureMessage ?? "Test failed but no exception was captured.";
                                string stackTrace = interceptingBus.LastFailureStackTrace;
                                bool isAssertionFailure = interceptingBus.IsAssertionFailure;

                                string fullMessage = failureMsg +
                                    (stackTrace != null ? Environment.NewLine + "Stack Trace:" + Environment.NewLine + stackTrace : string.Empty);

                                if (isAssertionFailure)
                                {
                                    lastException = new XunitException(fullMessage);
                                }
                                else
                                {
                                    lastException = new InvalidOperationException(fullMessage);
                                }
                            }

                            Trace.WriteLine($"##vso[task.logdetail]RetryTheory test failed on attempt {attempt} with exception type: {lastException?.GetType().FullName ?? "null"}");

                            var shouldRetry = ShouldRetry(lastException);
                            Trace.WriteLine($"##vso[task.logdetail]RetryTheory ShouldRetry={shouldRetry}");

                            if (!shouldRetry)
                            {
                                Trace.WriteLine($"##vso[task.logissue type=warning]Test '{TestMethod.Method.Name}' failed with non-retriable exception. Skipping retries.");

                                if (lastException != null)
                                {
                                    aggregator.Add(lastException);
                                }

                                return runSummary;
                            }

                            Trace.WriteLine($"##vso[task.logdetail]RetryTheory: Waiting {_delayMs}ms before retry...");
                            await Task.Delay(_delayMs, cancellationTokenSource.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            lastException = ex;

                            if (ShouldRetry(ex))
                            {
                                Trace.WriteLine($"##vso[task.logdetail]RetryTheory: Caught exception {ex.GetType().Name}, waiting {_delayMs}ms before retry...");
                                await Task.Delay(_delayMs, cancellationTokenSource.Token);
                                continue;
                            }

                            throw;
                        }
                    }
                }
            }

            // All retries exhausted
            Trace.WriteLine($"##vso[task.logissue type=error]Test '{TestMethod.Method.Name}' failed after {_maxRetries} attempts.");
            diagnosticMessageSink.OnMessage(
                new DiagnosticMessage($"[RetryTheory] Test '{TestMethod.Method.Name}' failed after {_maxRetries} attempts. Last exception: {lastException?.Message ?? "unknown"}"));

            if (lastException != null)
            {
                aggregator.Add(lastException);
            }

            return runSummary;
        }

        public override void Serialize(IXunitSerializationInfo data)
        {
            base.Serialize(data);
            data.AddValue(nameof(_maxRetries), _maxRetries);
            data.AddValue(nameof(_delayMs), _delayMs);
            data.AddValue(nameof(_retryOnAssertionFailure), _retryOnAssertionFailure);
        }

        public override void Deserialize(IXunitSerializationInfo data)
        {
            base.Deserialize(data);
            _maxRetries = data.GetValue<int>(nameof(_maxRetries));
            _delayMs = data.GetValue<int>(nameof(_delayMs));
            _retryOnAssertionFailure = data.GetValue<bool>(nameof(_retryOnAssertionFailure));
        }

        /// <summary>
        /// Determines if an exception should trigger a retry.
        /// </summary>
        private bool ShouldRetry(Exception ex)
        {
            if (ex == null)
            {
                return true;
            }

            while (ex is AggregateException aggEx && aggEx.InnerExceptions.Count == 1)
            {
                ex = aggEx.InnerException;
            }

            if (ex is XunitException)
            {
                return _retryOnAssertionFailure;
            }

            return true;
        }

        /// <summary>
        /// Message bus that intercepts ONLY failure messages (ITestFailed).
        /// </summary>
        private class FailureInterceptingMessageBus : IMessageBus
        {
            private readonly IMessageBus _innerBus;

            public FailureInterceptingMessageBus(IMessageBus innerBus)
            {
                _innerBus = innerBus;
            }

            public string LastFailureMessage { get; private set; }

            public string LastFailureStackTrace { get; private set; }

            public bool IsAssertionFailure { get; private set; }

            public bool QueueMessage(IMessageSinkMessage message)
            {
                if (message is ITestFailed failed)
                {
                    LastFailureMessage = failed.Messages != null && failed.Messages.Length > 0
                        ? string.Join(Environment.NewLine, failed.Messages)
                        : failed.ExceptionTypes != null && failed.ExceptionTypes.Length > 0
                            ? string.Join(", ", failed.ExceptionTypes)
                            : "Unknown failure";

                    LastFailureStackTrace = failed.StackTraces != null && failed.StackTraces.Length > 0
                        ? string.Join(Environment.NewLine, failed.StackTraces)
                        : null;

                    IsAssertionFailure = failed.ExceptionTypes != null &&
                        failed.ExceptionTypes.Length > 0 &&
                        (failed.ExceptionTypes[0].Contains("Xunit", StringComparison.Ordinal) ||
                         failed.ExceptionTypes[0].Contains("Assert", StringComparison.Ordinal));

                    return true; // Swallow the failure
                }

                return _innerBus.QueueMessage(message);
            }

            public void Dispose()
            {
            }
        }
    }
}
