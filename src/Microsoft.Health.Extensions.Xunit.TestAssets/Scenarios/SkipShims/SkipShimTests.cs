// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.SkipShims
{
    /// <summary>
    /// Exercises the source-compatibility shims that keep the repository's legacy
    /// <c>SkippableFact</c> and <c>Skip.If</c> call sites working on xunit.v3.
    /// </summary>
    /// <remarks>
    /// The shims stand in for a package that no longer exists on v3, so nothing but these scenarios
    /// says whether they still do what the several hundred call sites using them assume. The
    /// distinction that matters is skipped against failed: a conditional skip reported as a failure
    /// would break every leg that skips tests it cannot run, and a skip that silently passed would
    /// let a test that never ran count as one that did.
    /// </remarks>
    public class SkipShimTests
    {
        /// <summary>
        /// A true condition skips, and is reported as skipped rather than as a failure.
        /// </summary>
        [SkippableFact]
        public void SkipIfTrue_IsSkipped()
        {
            Skip.If(true, "the condition held");
            Assert.Fail("Skip.If(true) should have stopped the test before this point.");
        }

        /// <summary>
        /// A false condition does not skip, so the test runs to completion and passes.
        /// </summary>
        [SkippableFact]
        public void SkipIfFalse_Runs()
        {
            Skip.If(false, "the condition did not hold");
            Assert.True(true);
        }

        /// <summary>
        /// <see cref="Skip.IfNot"/> is the inverse, so a false condition skips.
        /// </summary>
        [SkippableFact]
        public void SkipIfNotFalse_IsSkipped()
        {
            Skip.IfNot(false, "the condition did not hold");
            Assert.Fail("Skip.IfNot(false) should have stopped the test before this point.");
        }

        /// <summary>
        /// <see cref="Skip.IfNot"/> with a true condition does not skip.
        /// </summary>
        [SkippableFact]
        public void SkipIfNotTrue_Runs()
        {
            Skip.IfNot(true, "the condition held");
            Assert.True(true);
        }

        /// <summary>
        /// The reason given at the call site has to reach the report, otherwise a skipped leg gives
        /// no indication of why its tests did not run.
        /// </summary>
        [SkippableFact]
        public void SkipWithReason_IsSkipped()
        {
            Skip.If(true, "a distinctive skip reason");
            Assert.Fail("Skip.If(true) should have stopped the test before this point.");
        }

        /// <summary>
        /// The theory shim skips per data row, so the same method both skips and passes.
        /// </summary>
        /// <param name="skip">Whether this row should skip.</param>
        [SkippableTheory]
        [InlineData(true)]
        [InlineData(false)]
        public void SkippableTheory_SkipsPerRow(bool skip)
        {
            Skip.If(skip, "the row asked to be skipped");
            Assert.False(skip);
        }
    }
}
