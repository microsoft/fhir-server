// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Covers how a failure is recognised as an assertion failure when the retry attempt itself has
    /// to classify it.
    /// </summary>
    /// <remarks>
    /// <c>RetryOnAssertionFailure</c> is what tells a retrying test not to spend attempts on a failure
    /// that will fail the same way every time. It can only do that if an assertion failure is still
    /// recognised once something has wrapped it, which is the normal shape rather than an exotic one:
    /// anything awaited through <see cref="Task.WhenAll(Task[])"/>, a parallel helper, or a teardown
    /// that throws alongside the test arrives as an <see cref="AggregateException"/>. Two places make
    /// this call - the message bus, from the reported type names, and the attempt itself, from the
    /// exception - and they have to agree, or whether the policy is honoured comes down to which one
    /// happened to see the failure.
    /// </remarks>
    public class RetryAssertionClassificationTests
    {
        /// <summary>
        /// The plain case: an assertion failure with nothing around it.
        /// </summary>
        [Fact]
        public void GivenABareAssertionFailure_WhenItIsClassified_ThenItIsAnAssertionFailure()
        {
            Assert.True(RetryTestCase.ContainsAssertionFailure(new XunitException("assertion")));
        }

        /// <summary>
        /// One wrapper deep, which is what awaiting a single faulted task produces.
        /// </summary>
        [Fact]
        public void GivenAnAssertionFailureWrappedAlone_WhenItIsClassified_ThenItIsAnAssertionFailure()
        {
            Assert.True(RetryTestCase.ContainsAssertionFailure(
                new AggregateException(new XunitException("assertion"))));
        }

        /// <summary>
        /// The case that unwrapping only single-inner aggregates missed. A test that awaits several
        /// tasks, or whose teardown throws alongside the assertion, produces an aggregate holding more
        /// than one exception, and the assertion is no less deterministic for having company.
        /// </summary>
        [Fact]
        public void GivenAnAssertionFailureAmongOthers_WhenItIsClassified_ThenItIsStillAnAssertionFailure()
        {
            Assert.True(RetryTestCase.ContainsAssertionFailure(
                new AggregateException(
                    new InvalidOperationException("the connection dropped"),
                    new XunitException("assertion"))));
        }

        /// <summary>
        /// Nesting is not always flat: an aggregate can hold an aggregate.
        /// </summary>
        [Fact]
        public void GivenAnAssertionFailureNestedDeeply_WhenItIsClassified_ThenItIsStillAnAssertionFailure()
        {
            Assert.True(RetryTestCase.ContainsAssertionFailure(
                new AggregateException(
                    new InvalidOperationException("the connection dropped"),
                    new AggregateException(new XunitException("assertion")))));
        }

        /// <summary>
        /// An assertion reached through an ordinary inner exception rather than an aggregate.
        /// </summary>
        [Fact]
        public void GivenAnAssertionFailureAsAnInnerException_WhenItIsClassified_ThenItIsStillAnAssertionFailure()
        {
            Assert.True(RetryTestCase.ContainsAssertionFailure(
                new InvalidOperationException("wrapped", new XunitException("assertion"))));
        }

        /// <summary>
        /// The other direction matters just as much. Classifying a transient failure as an assertion
        /// would cost it every retry it was given the attribute for.
        /// </summary>
        [Fact]
        public void GivenNoAssertionFailureAnywhere_WhenItIsClassified_ThenItIsNotAnAssertionFailure()
        {
            Assert.False(RetryTestCase.ContainsAssertionFailure(
                new AggregateException(
                    new InvalidOperationException("the connection dropped"),
                    new TimeoutException("it took too long"))));
        }

        /// <summary>
        /// A timeout needs no carve-out here, unlike in the message bus, which matches on type names
        /// and so has to exclude the one whose name contains "Xunit". This classifies by type, and the
        /// invariant that makes that safe is pinned here: <c>TestTimeoutException</c> does not derive
        /// from <see cref="XunitException"/>, so a timeout stays retryable - which is the whole point,
        /// as a test that ran long because a dependency was slow once is what retrying is for. The
        /// type cannot be constructed from here, so the relationship is asserted rather than exercised.
        /// </summary>
        [Fact]
        public void GivenTheTimeoutType_WhenItIsInspected_ThenItIsNotAnAssertionType()
        {
            Assert.False(typeof(XunitException).IsAssignableFrom(typeof(TestTimeoutException)));
        }

        /// <summary>
        /// Nothing to classify is not an assertion failure, and must not throw on the way to saying so.
        /// </summary>
        [Fact]
        public void GivenNoException_WhenItIsClassified_ThenItIsNotAnAssertionFailure()
        {
            Assert.False(RetryTestCase.ContainsAssertionFailure(null));
        }
    }
}
