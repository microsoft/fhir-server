// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.ExecutionContextFlow
{
    /// <summary>
    /// Pins what happens to a value the class fixture constructor wrote into the execution context.
    /// </summary>
    /// <remarks>
    /// Under xunit.v2 the custom executor ran test methods in the execution context captured just
    /// after each class fixture was built, so this value was readable here. xunit.v3 builds fixtures
    /// inside an async method, and an async method's state machine restores the caller's execution
    /// context as it returns, so the write is discarded before any runner code can capture it. This
    /// test records that loss deliberately: it fails if the flow ever comes back, which would mean
    /// the note in <c>FixtureArgumentSetClassRunner</c> and the fixtures written against it are out
    /// of date.
    /// </remarks>
    public class AsyncLocalFlowTests : IClassFixture<AsyncLocalWritingFixture>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncLocalFlowTests"/> class.
        /// </summary>
        /// <param name="fixture">The fixture whose constructor writes the probe value.</param>
        public AsyncLocalFlowTests(AsyncLocalWritingFixture fixture)
        {
            Fixture = fixture;
        }

        /// <summary>
        /// Gets the fixture this class was constructed with.
        /// </summary>
        protected AsyncLocalWritingFixture Fixture { get; }

        /// <summary>
        /// Fails if a value written to the execution context by a class fixture constructor becomes
        /// readable from a test method again.
        /// </summary>
        [Fact]
        public void DoesNotSeeTheValueWrittenByTheFixtureConstructor()
        {
            Assert.Null(AsyncLocalProbe.Value.Value);
        }
    }
}
