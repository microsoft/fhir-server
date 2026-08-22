// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Covers which exceptions out of fixture argument set expansion are reported as failing test
    /// cases and which go back to xunit unchanged.
    /// </summary>
    /// <remarks>
    /// Reporting a discovery fault as a test is what stops a broken expansion from leaving a run
    /// green with its tests silently absent, and the scenario assets cover that. The exclusions
    /// cannot be reached the same way: cancellation would have to arrive at the moment a class is
    /// being expanded, which no scenario can arrange reliably. The decision is therefore made by one
    /// function so that every case can be pinned directly, because getting an exclusion wrong is not
    /// visible in a passing run - it invents a red test naming a class with nothing wrong with it.
    /// </remarks>
    public class DiscoveryFaultDispositionTests
    {
        /// <summary>
        /// A cancelled run is not a discovery fault. Without this, pressing Ctrl+C or hitting a
        /// runner-imposed timeout would produce a failing test case for whichever class happened to
        /// be mid-expansion, and a cancelled run would be indistinguishable from a broken one.
        /// </summary>
        [Fact]
        public void GivenACancellation_WhenExpansionEnds_ThenItIsRethrownRatherThanReportedAsATest()
        {
            Assert.True(CustomXunitTestFrameworkDiscoverer.ShouldRethrowRatherThanReport(
                new OperationCanceledException(),
                isCallbackFailure: false,
                isCancellationRequested: true));
        }

        /// <summary>
        /// <see cref="TaskCanceledException"/> derives from <see cref="OperationCanceledException"/>
        /// and is what an awaited task throws when the run is cancelled, so it has to be treated the
        /// same way. Matching on the exact type instead would let the common case through.
        /// </summary>
        [Fact]
        public void GivenATaskCancellation_WhenExpansionEnds_ThenItIsRethrownRatherThanReportedAsATest()
        {
            Assert.True(CustomXunitTestFrameworkDiscoverer.ShouldRethrowRatherThanReport(
                new TaskCanceledException(),
                isCallbackFailure: false,
                isCancellationRequested: true));
        }

        /// <summary>
        /// A failure raised by xunit's own callback is not a fault in expanding argument sets, and
        /// the only way to report it would be to hand that same callback another test case.
        /// </summary>
        [Fact]
        public void GivenAFailureFromTheCallback_WhenExpansionEnds_ThenItIsRethrownRatherThanReportedAsATest()
        {
            Assert.True(CustomXunitTestFrameworkDiscoverer.ShouldRethrowRatherThanReport(
                new InvalidOperationException("the sink threw"),
                isCallbackFailure: true,
                isCancellationRequested: false));
        }

        /// <summary>
        /// Anything else belongs to the expansion, and is the case the whole mechanism exists for:
        /// reported as a failing test case so it reaches the results and the exit code instead of
        /// leaving the run green with the class's tests missing.
        /// </summary>
        [Fact]
        public void GivenAnExpansionFailure_WhenExpansionEnds_ThenItIsReportedAsATest()
        {
            Assert.False(CustomXunitTestFrameworkDiscoverer.ShouldRethrowRatherThanReport(
                new InvalidOperationException("the argument set was misdeclared"),
                isCallbackFailure: false,
                isCancellationRequested: false));
        }

        /// <summary>
        /// An <see cref="OperationCanceledException"/> out of a run nobody cancelled did not come from
        /// the runner: expansion reads the attributes declared on the class, and one of those is free
        /// to throw it for reasons of its own. Rethrowing it would drop the class and leave the run
        /// green with its tests missing, which is what reporting discovery faults exists to prevent.
        /// </summary>
        [Fact]
        public void GivenACancellationExceptionButNoCancellation_WhenExpansionEnds_ThenItIsReportedAsATest()
        {
            Assert.False(CustomXunitTestFrameworkDiscoverer.ShouldRethrowRatherThanReport(
                new OperationCanceledException("an attribute gave up waiting"),
                isCallbackFailure: false,
                isCancellationRequested: false));
        }

        /// <summary>
        /// The derived type gets the same treatment, because it is the one an attribute that awaits
        /// anything with a timeout will actually throw.
        /// </summary>
        [Fact]
        public void GivenATaskCancellationButNoCancellation_WhenExpansionEnds_ThenItIsReportedAsATest()
        {
            Assert.False(CustomXunitTestFrameworkDiscoverer.ShouldRethrowRatherThanReport(
                new TaskCanceledException("an attribute gave up waiting"),
                isCallbackFailure: false,
                isCancellationRequested: false));
        }

        /// <summary>
        /// Cancellation wins even when it also reached the callback, because there is nothing to
        /// report a cancelled run against.
        /// </summary>
        [Fact]
        public void GivenACancellationFromTheCallback_WhenExpansionEnds_ThenItIsRethrownRatherThanReportedAsATest()
        {
            Assert.True(CustomXunitTestFrameworkDiscoverer.ShouldRethrowRatherThanReport(
                new OperationCanceledException(),
                isCallbackFailure: true,
                isCancellationRequested: true));
        }
    }
}
