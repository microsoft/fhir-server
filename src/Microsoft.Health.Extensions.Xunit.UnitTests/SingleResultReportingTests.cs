// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Pins the rule that resolves two attempts competing to report a single test.
    /// </summary>
    /// <remarks>
    /// An intercepting bus replays anything it still holds when it is disposed, so that a failure
    /// can never be lost. The cost of that fail-safe is that a caller cannot stay silent about a
    /// bus by saying nothing: the loser of the competition has to be discarded explicitly, or it
    /// publishes a second result moments after the winner. When the loser is holding an abstention
    /// and the winner is a failure, that second result is a skip arriving after a failure - which
    /// is how a red test can be read as green.
    /// </remarks>
    [Trait("Category", "UnitTests")]
    public class SingleResultReportingTests
    {
        [Fact]
        public void GivenAnEarlierFailureAndAnAbstainingCurrentAttempt_WhenTheEarlierAttemptWins_ThenOnlyTheFailureIsPublished()
        {
            var inner = new RecordingMessageBus();
            RetryTestCase.FailureInterceptingMessageBus earlier = DeferFailure(inner);
            RetryTestCase.FailureInterceptingMessageBus current = DeferAbstention(inner);

            RetryTestCase.ReportSingleResult(NoResultOutcome.ReplayEarlierAttempt, current, earlier);

            Assert.Equal(new[] { nameof(ITestFailed) }, inner.PublishedKinds);

            // Disposal is the fail-safe, and it must have nothing left to fall back on.
            current.Dispose();
            earlier.Dispose();
            Assert.Equal(new[] { nameof(ITestFailed) }, inner.PublishedKinds);
        }

        [Fact]
        public void GivenBothAttemptsHoldingMessages_WhenTheCurrentAttemptWins_ThenOnlyItsMessagesArePublished()
        {
            var inner = new RecordingMessageBus();
            RetryTestCase.FailureInterceptingMessageBus earlier = DeferFailure(inner);
            RetryTestCase.FailureInterceptingMessageBus current = DeferFailure(inner);

            RetryTestCase.ReportSingleResult(NoResultOutcome.ReplayCurrentAttempt, current, earlier);

            Assert.Equal(new[] { nameof(ITestFailed) }, inner.PublishedKinds);

            current.Dispose();
            earlier.Dispose();
            Assert.Equal(new[] { nameof(ITestFailed) }, inner.PublishedKinds);
        }

        [Fact]
        public void GivenNothingObservedAFailure_WhenThereIsNothingToReport_ThenTheCurrentAbstentionSurvivesToBePublished()
        {
            var inner = new RecordingMessageBus();
            RetryTestCase.FailureInterceptingMessageBus current = DeferAbstention(inner);

            RetryTestCase.ReportSingleResult(NoResultOutcome.ReportNothing, current, earlierAttempt: null);

            // The abstention is the only result this test has, so it is left for disposal to publish
            // rather than being dropped.
            Assert.Empty(inner.PublishedKinds);
            current.Dispose();
            Assert.Equal(new[] { nameof(ITestSkipped) }, inner.PublishedKinds);
        }

        [Fact]
        public void GivenAnEarlierFailureThatLostToNothing_WhenThereIsNothingToReport_ThenItIsNotPublished()
        {
            var inner = new RecordingMessageBus();
            RetryTestCase.FailureInterceptingMessageBus earlier = DeferFailure(inner);

            RetryTestCase.ReportSingleResult(NoResultOutcome.ReportNothing, currentAttempt: null, earlierAttempt: earlier);

            earlier.Dispose();
            Assert.Empty(inner.PublishedKinds);
        }

        [Fact]
        public void GivenAMissingBus_WhenTheRuleIsApplied_ThenItReportsThatTheRunMayContinue()
        {
            Assert.True(RetryTestCase.ReportSingleResult(NoResultOutcome.ReplayEarlierAttempt, currentAttempt: null, earlierAttempt: null));
            Assert.True(RetryTestCase.ReportSingleResult(NoResultOutcome.ReplayCurrentAttempt, currentAttempt: null, earlierAttempt: null));
        }

        [Fact]
        public void GivenTheUnderlyingBusAsksToStop_WhenTheWinnerIsReplayed_ThenTheRequestIsPassedOn()
        {
            var inner = new RecordingMessageBus { ContinueRunning = false };
            RetryTestCase.FailureInterceptingMessageBus earlier = DeferFailure(inner);

            Assert.False(RetryTestCase.ReportSingleResult(NoResultOutcome.ReplayEarlierAttempt, currentAttempt: null, earlierAttempt: earlier));
        }

        [Fact]
        public void GivenADeliberateHandoffToDisposal_WhenTheBusIsDisposed_ThenItPublishesWithoutClaimingAnInternalError()
        {
            var inner = new RecordingMessageBus();
            RetryTestCase.FailureInterceptingMessageBus current = DeferAbstention(inner);

            RetryTestCase.ReportSingleResult(NoResultOutcome.ReportNothing, current, earlierAttempt: null);

            string console = CaptureConsole(current.Dispose);

            Assert.Equal(new[] { nameof(ITestSkipped) }, inner.PublishedKinds);
            Assert.DoesNotContain("Internal error", console, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenMessagesNobodyResolved_WhenTheBusIsDisposed_ThenItStillPublishesAndSaysSo()
        {
            var inner = new RecordingMessageBus();
            RetryTestCase.FailureInterceptingMessageBus orphaned = DeferFailure(inner);

            string console = CaptureConsole(orphaned.Dispose);

            Assert.Equal(new[] { nameof(ITestFailed) }, inner.PublishedKinds);
            Assert.Contains("Internal error", console, StringComparison.Ordinal);
        }

        private static string CaptureConsole(Action action)
        {
            System.IO.TextWriter original = Console.Out;
            var captured = new System.IO.StringWriter();

            try
            {
                Console.SetOut(captured);
                action();
            }
            finally
            {
                Console.SetOut(original);
            }

            return captured.ToString();
        }

        private static RetryTestCase.FailureInterceptingMessageBus DeferFailure(RecordingMessageBus inner)
        {
            var bus = new RetryTestCase.FailureInterceptingMessageBus(inner, deferFailures: true);
            bus.QueueMessage(new StubTestFailed());
            return bus;
        }

        private static RetryTestCase.FailureInterceptingMessageBus DeferAbstention(RecordingMessageBus inner)
        {
            var bus = new RetryTestCase.FailureInterceptingMessageBus(inner, deferFailures: true, deferAbstentions: true);
            bus.QueueMessage(new StubTestSkipped());
            return bus;
        }

        private sealed class RecordingMessageBus : IMessageBus
        {
            private readonly List<string> _published = new List<string>();

            public bool ContinueRunning { get; set; } = true;

            public IReadOnlyList<string> PublishedKinds => _published
                .ToList();

            public bool QueueMessage(IMessageSinkMessage message)
            {
                _published.Add(message switch
                {
                    ITestFailed => nameof(ITestFailed),
                    ITestSkipped => nameof(ITestSkipped),
                    _ => message.GetType().Name,
                });

                return ContinueRunning;
            }

            public void Dispose()
            {
            }
        }

        private abstract class StubResultMessage
        {
            public string AssemblyUniqueID => "assembly";

            public string TestCollectionUniqueID => "collection";

            public string TestClassUniqueID => "class";

            public string TestMethodUniqueID => "method";

            public string TestCaseUniqueID => "case";

            public string TestUniqueID => "test";

            public decimal ExecutionTime => 0m;

            public string Output => string.Empty;

            public string[] Warnings => null;

            public DateTimeOffset FinishTime => DateTimeOffset.MinValue;

            public string ToJson() => "{}";
        }

        private sealed class StubTestFailed : StubResultMessage, ITestFailed
        {
            public FailureCause Cause => FailureCause.Assertion;

            public int[] ExceptionParentIndices => new[] { -1 };

            public string[] ExceptionTypes => new[] { typeof(InvalidOperationException).FullName };

            public string[] Messages => new[] { "stub failure" };

            public string[] StackTraces => new string[] { null };
        }

        private sealed class StubTestSkipped : StubResultMessage, ITestSkipped
        {
            public string Reason => "stub skip";
        }
    }
}
